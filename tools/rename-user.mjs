/* One-off maintenance: rename an account and re-derive its login verifier.
   The verifier is PBKDF2(password, "social.login.v1|username"), so a username
   change without a re-derivation would lock the account out.

   usage:
     node tools/rename-user.mjs <old> <new> [displayName] [password]
   If the password is omitted the verifier is left untouched (the account can
   then only be recovered with its recovery code).
*/
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';

const [, , oldName, newName, displayName, password] = process.argv;
if (!oldName || !newName) {
  console.error('usage: node tools/rename-user.mjs <old> <new> [displayName] [password]');
  process.exit(1);
}

const root = path.resolve(process.argv[1], '..', '..');
const dataDir = path.join(root, 'bin', 'data');
const usersFile = path.join(dataDir, 'users.json');

function readIni(p) {
  const map = {};
  if (!fs.existsSync(p)) return map;
  for (const line of fs.readFileSync(p, 'utf8').split(/\r?\n/)) {
    const m = /^\s*([A-Za-z0-9_.]+)\s*=\s*(.*?)\s*$/.exec(line);
    if (m) map[m[1].toLowerCase()] = m[2];
  }
  return map;
}

const cfg = readIni(path.join(root, 'bin', 'config.ini'));
const iterations = Number(cfg.kdfiterations || 12000);

const users = JSON.parse(fs.readFileSync(usersFile, 'utf8'));
const oldKey = oldName.toLowerCase();
const newKey = newName.toLowerCase();

const user = users.find(u => String(u.username).toLowerCase() === oldKey);
if (!user) {
  console.error('user "' + oldName + '" not found');
  process.exit(1);
}

// another account already using the target name (the account itself is fine)
if (users.some(u => u !== user && String(u.username).toLowerCase() === newKey)) {
  console.error('user "' + newName + '" already exists');
  process.exit(1);
}

// keep the spelling the operator asked for; lookups stay case-insensitive
user.username = newName;
if (displayName) user.displayName = displayName;

if (password) {
  // verifier = PBKDF2(password, "social.login.v1|username")
  const verifier = crypto
    .pbkdf2Sync(password, Buffer.from('social.login.v1|' + newKey, 'utf8'), iterations, 32, 'sha256')
    .toString('hex');
  // stored = PBKDF2(verifier_hex_as_utf8, salt, iterations)
  user.verifier = crypto
    .pbkdf2Sync(Buffer.from(verifier, 'utf8'), Buffer.from(user.salt, 'hex'), iterations, 32, 'sha256')
    .toString('hex');
}

const text = JSON.stringify(users, null, 2);
for (const target of [usersFile, usersFile + '.bak']) {
  try { fs.writeFileSync(target, text, 'utf8'); } catch { }
}

console.log('renamed #' + user.id + ' ' + oldKey + ' -> ' + user.username +
  (password ? ' (verifier re-derived)' : ' (verifier untouched: use the recovery code)'));
