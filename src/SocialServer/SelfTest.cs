using System;
using System.Globalization;
using System.IO;
using System.Text;
using Social.Core;

namespace Social
{
    /// <summary>
    /// Dependency free smoke tests for the parts that are easy to break silently:
    /// crypto vectors, envelope tampering, JSON, captcha markup, storage round trip.
    /// Run with: SocialServer.exe --selftest
    /// </summary>
    public static class SelfTest
    {
        private static int _failed;

        public static int Run()
        {
            Console.WriteLine("Social Server self-test");
            Console.WriteLine("-----------------------");
            _failed = 0;

            TestSha256();
            TestHmac();
            TestPbkdf2();
            TestSeal();
            TestSignature();
            TestJson();
            TestCaptcha();
            TestStore();

            Console.WriteLine("-----------------------");
            if (_failed == 0) Console.WriteLine("ALL TESTS PASSED");
            else Console.WriteLine(_failed + " TEST(S) FAILED");
            return _failed == 0 ? 0 : 1;
        }

        private static void Check(string name, bool ok, string detail)
        {
            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name + (string.IsNullOrEmpty(detail) ? "" : "  (" + detail + ")"));
            if (!ok) _failed++;
        }

        private static void TestSha256()
        {
            string hex = Crypto.Hex(Crypto.Sha256(Encoding.UTF8.GetBytes("abc")));
            Check("SHA-256 abc", hex == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hex);
        }

        private static void TestHmac()
        {
            string hex = Crypto.Hex(Crypto.Hmac(Encoding.UTF8.GetBytes("key"),
                Encoding.UTF8.GetBytes("The quick brown fox jumps over the lazy dog")));
            Check("HMAC-SHA256", hex == "f7bc83f430538424b13298e6aa6fb143ef4d59a14946175997479dbc2d1a3cd8", hex);
        }

        private static void TestPbkdf2()
        {
            string hex = Crypto.Hex(Crypto.Pbkdf2(Encoding.UTF8.GetBytes("password"),
                Encoding.UTF8.GetBytes("salt"), 1, 32));
            Check("PBKDF2-SHA256", hex == "120fb6cffcf8b32c43e7225256c4f837a86548c92ccc35480805987cb70be17b", hex);
        }

        private static void TestSeal()
        {
            byte[] key = Crypto.RandomBytes(32);
            byte[] plain = Encoding.UTF8.GetBytes("hello encrypted world");
            string envelope = Crypto.Seal(key, plain);
            byte[] opened = Crypto.Open(key, envelope);
            Check("seal/open round trip", opened != null && Encoding.UTF8.GetString(opened) == "hello encrypted world", "");

            byte[] tampered = Convert.FromBase64String(envelope);
            tampered[tampered.Length / 2] ^= 0x01;
            byte[] bad = Crypto.Open(key, Convert.ToBase64String(tampered));
            Check("tampered envelope rejected", bad == null, "");
        }

        private static void TestSignature()
        {
            byte[] key = Crypto.RandomBytes(32);
            string sig = Crypto.Signature(key, "POST", "/api/post", "1700000000", "0011223344556677",
                                          Encoding.UTF8.GetBytes("{\"a\":1}"));
            string again = Crypto.Signature(key, "POST", "/api/post", "1700000000", "0011223344556677",
                                            Encoding.UTF8.GetBytes("{\"a\":1}"));
            Check("signature deterministic", sig == again && sig.Length == 64, sig.Substring(0, 16) + "...");
            Check("fixed equals", Crypto.FixedEquals(sig, again) && !Crypto.FixedEquals(sig, sig.Substring(1) + "0"), "");
        }

        private static void TestJson()
        {
            string text = "{\"a\":1,\"b\":[true,null,\"x\\u00e9\"],\"c\":{\"d\":-2.5}}";
            try
            {
                System.Collections.Generic.Dictionary<string, object> d = Json.ParseObject(text, 16);
                System.Collections.Generic.List<object> arr = Json.Get(d, "b") as System.Collections.Generic.List<object>;
                bool ok = Json.GetInt(d, "a", 0) == 1 && arr != null && arr.Count == 3;
                string round = Json.Stringify(d);
                System.Collections.Generic.Dictionary<string, object> d2 = Json.ParseObject(round, 16);
                ok = ok && Json.GetDouble(d2, "a", 0) == 1;
                Check("JSON parse/stringify", ok, "");
            }
            catch (Exception ex)
            {
                Check("JSON parse/stringify", false, ex.Message);
            }
        }

        private static void TestCaptcha()
        {
            string code = Captcha.NewCode();
            string svg = Captcha.Svg(code);
            Check("captcha has no <text>", svg.IndexOf("<text", StringComparison.OrdinalIgnoreCase) < 0 && svg.IndexOf("<svg", StringComparison.Ordinal) >= 0, code);
        }

        private static void TestStore()
        {
            string dir = Path.Combine(Path.GetTempPath(), "social-selftest-" + Guid.NewGuid().ToString("N"));
            try
            {
                Config cfg = new Config();
                cfg.BaseDir = dir;
                cfg.DataDir = Path.Combine(dir, "data");
                cfg.MediaDir = Path.Combine(cfg.DataDir, "media");
                cfg.BackupDir = Path.Combine(cfg.DataDir, "backups");
                cfg.KdfIterations = 1200;
                cfg.NotificationsPerUser = 50;
                Directory.CreateDirectory(cfg.MediaDir);

                SocialStore store = new SocialStore(cfg);
                store.Load();

                User alice = store.CreateUser("alice", "Alice", Crypto.RandomHex(32), Crypto.NewRecoveryCode(), "admin", "ip1");
                User bob = store.CreateUser("bob", "Bob", Crypto.RandomHex(32), Crypto.NewRecoveryCode(), "user", "ip2");
                Check("create users", alice != null && bob != null && store.CountUsers() == 2, "");

                Post post = store.CreatePost(alice.Id, 0, 0, "hello wall", "", "public");
                Comment comment = store.AddComment(post.Id, bob.Id, "nice");
                bool liked = store.ToggleLike(post.Id, bob.Id);
                string fr = store.SendFriendRequest(bob.Id, alice.Id);
                bool accepted = store.AcceptFriend(bob.Id, alice.Id);
                Message msg = store.CreateMessage(bob.Id, alice.Id, "hi");
                Group group = store.CreateGroup(alice.Id, "Test Group", "desc", "public");
                store.Notify(alice.Id, bob.Id, "like", "liked your post", "#/post/" + post.Id);

                Check("post/comment/like", post != null && comment != null && liked && post.Likes.Count == 1, "");
                Check("friend request accepted", fr == "pending" && accepted && store.AreFriends(alice.Id, bob.Id), fr);
                Check("message", msg != null && store.UnreadMessages(alice.Id) == 1, "");
                Check("group", group != null && store.JoinGroup(group.Id, bob.Id), "");
                Check("notification", store.UnreadNotifications(alice.Id) >= 1, "");

                store.SaveNow();
                SocialStore loaded = new SocialStore(cfg);
                loaded.Load();
                bool ok = loaded.CountUsers() == 2 && loaded.CountAllPosts() == 1 &&
                          loaded.CountAllComments() == 1 && loaded.CountAllLikes() == 1 &&
                          loaded.AreFriends(alice.Id, bob.Id) && loaded.UnreadMessages(alice.Id) == 1 &&
                          loaded.CountGroups() == 1 && loaded.UnreadNotifications(alice.Id) >= 1;
                Check("store round trip", ok, "users=" + loaded.CountUsers() + " posts=" + loaded.CountAllPosts());

                // corrupt the main file, .bak must rescue the data
                string usersFile = Path.Combine(cfg.DataDir, "users.json");
                File.WriteAllText(usersFile, "{broken", Encoding.UTF8);
                SocialStore recovered = new SocialStore(cfg);
                recovered.Load();
                Check("corrupt file recovered from .bak", recovered.CountUsers() == 2, "users=" + recovered.CountUsers());
            }
            catch (Exception ex)
            {
                Check("store scenario", false, ex.Message);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
