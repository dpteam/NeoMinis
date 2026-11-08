/* =========================================================================
   I18N - tiny ES5 dictionary engine. Fallback: selected -> English -> Russian
   -> key. Add a language by dropping another object into DICT.
   ========================================================================= */
var I18N = (function () {
  'use strict';

  var DICT = {
    en: {
      'app.loading': 'Loading...',
      'app.name': 'Social',
      'app.tagline': 'Your own social network',
      'nav.feed': 'Feed', 'nav.friends': 'Friends', 'nav.messages': 'Messages',
      'nav.people': 'People', 'nav.groups': 'Groups', 'nav.notifications': 'Notifications',
      'nav.settings': 'Settings', 'nav.admin': 'Admin', 'nav.search': 'Search',
      'nav.logout': 'Sign out', 'nav.profile': 'My profile', 'nav.online': 'Online',
      'nav.menu': 'Menu', 'nav.more': 'More',

      'auth.login': 'Sign in', 'auth.register': 'Create account', 'auth.recover': 'Recover',
      'auth.username': 'Username', 'auth.password': 'Password', 'auth.password2': 'Repeat password',
      'auth.displayName': 'Display name', 'auth.captcha': 'Captcha', 'auth.captchaHint': 'Type the digits',
      'auth.invite': 'Invite code', 'auth.signIn': 'Sign in', 'auth.createAccount': 'Create account',
      'auth.noAccount': 'No account yet?', 'auth.haveAccount': 'Already registered?',
      'auth.forgot': 'Forgot password?', 'auth.recoveryCode': 'Recovery code',
      'auth.recoveryHint': 'The code you saved when you registered.',
      'auth.newPassword': 'New password', 'auth.reset': 'Set new password',
      'auth.welcomeBack': 'Welcome back', 'auth.join': 'Join the network',
      'auth.terms': 'I agree to behave like a human being', 'auth.firstAdmin': 'The first account becomes administrator.',
      'auth.registrationClosed': 'Registration is closed.',
      'auth.loginTitle': 'Sign in', 'auth.registerTitle': 'Create an account', 'auth.recoverTitle': 'Recover access',
      'auth.loginSub': 'Use your username and password.', 'auth.registerSub': 'A minute and you are in.',
      'auth.recoverSub': 'Use the recovery code from registration.',
      'auth.saveRecovery': 'Save your recovery code', 'auth.recoverySaved': 'It is shown only once. Store it somewhere safe.',
      'auth.copy': 'Copy', 'auth.copied': 'Copied',
      'auth.passwordMismatch': 'Passwords do not match', 'auth.passwordShort': 'Password is too short (min 8)',
      'auth.usernameHint': '3-24 letters, digits, _ or .', 'auth.captchaWrong': 'Wrong captcha code',
      'auth.badCredentials': 'Wrong username or password', 'auth.locked': 'Too many attempts. Try again later.',
      'auth.banned': 'This account is blocked',

      'feed.title': 'Feed', 'feed.home': 'My feed', 'feed.global': 'Everything', 'feed.friends': 'Friends only',
      'feed.composerPlaceholder': 'What is on your mind?', 'feed.post': 'Post', 'feed.posting': 'Posting...',
      'feed.photo': 'Photo', 'feed.removePhoto': 'Remove photo', 'feed.visibility': 'Who can see it',
      'feed.public': 'Everyone', 'feed.friendsOnly': 'Friends', 'feed.private': 'Only me',
      'feed.empty': 'Nothing here yet', 'feed.emptyHint': 'Add a friend or write the first post.',
      'feed.loadMore': 'Load more', 'feed.noMore': 'That is all', 'feed.like': 'Like', 'feed.liked': 'Liked',
      'feed.comment': 'Comment', 'feed.comments': 'Comments', 'feed.share': 'Share', 'feed.copiedLink': 'Link copied',
      'feed.delete': 'Delete', 'feed.deleteConfirm': 'Delete this post?', 'feed.deleted': 'Post deleted',
      'feed.justNow': 'just now', 'feed.edited': 'edited', 'feed.writeOnWall': 'Write on the wall',
      'feed.posted': 'Posted', 'feed.commentPosted': 'Comment added',

      'post.title': 'Post', 'post.notFound': 'Post not found', 'post.commentPlaceholder': 'Write a comment...',
      'post.send': 'Send', 'post.commentsCount': '{0} comments',

      'profile.edit': 'Edit profile', 'profile.message': 'Message', 'profile.addFriend': 'Add friend',
      'profile.cancelRequest': 'Cancel request', 'profile.accept': 'Accept', 'profile.decline': 'Decline',
      'profile.removeFriend': 'Remove friend', 'profile.block': 'Block', 'profile.unblock': 'Unblock',
      'profile.friends': 'Friends', 'profile.posts': 'Posts', 'profile.likes': 'Likes',
      'profile.joined': 'Joined', 'profile.city': 'City', 'profile.country': 'Country',
      'profile.birthday': 'Birthday', 'profile.gender': 'Gender', 'profile.website': 'Website',
      'profile.about': 'About', 'profile.wall': 'Wall', 'profile.groups': 'Groups',
      'profile.writeOnWall': 'Write on the wall', 'profile.private': 'This profile is private',
      'profile.online': 'online', 'profile.offline': 'offline', 'profile.male': 'Male',
      'profile.female': 'Female', 'profile.other': 'Other', 'profile.since': 'Member since',
      'profile.saved': 'Profile saved', 'profile.requestSent': 'Friend request sent',

      'friends.title': 'Friends', 'friends.all': 'All friends', 'friends.incoming': 'Incoming requests',
      'friends.outgoing': 'Sent requests', 'friends.empty': 'No friends yet',
      'friends.emptyHint': 'Find people and send a request.', 'friends.requestSent': 'Request sent',
      'friends.accepted': 'Friend request accepted', 'friends.removed': 'Friend removed',
      'friends.declined': 'Request declined', 'friends.blocked': 'User blocked',

      'people.title': 'People', 'people.search': 'Search people', 'people.empty': 'Nobody found',
      'people.loadMore': 'Show more', 'people.total': '{0} people',

      'messages.title': 'Messages', 'messages.empty': 'No conversations yet', 'messages.select': 'Pick a conversation',
      'messages.placeholder': 'Write a message...', 'messages.send': 'Send', 'messages.unread': '{0} unread',
      'messages.noThreads': 'No messages yet', 'messages.sent': 'Message sent',

      'notifications.title': 'Notifications', 'notifications.empty': 'No notifications',
      'notifications.markAll': 'Mark all read', 'notifications.marked': 'All caught up',
      'notifications.new': 'new',

      'groups.title': 'Groups', 'groups.create': 'Create group', 'groups.name': 'Name',
      'groups.description': 'Description', 'groups.visibility': 'Visibility', 'groups.public': 'Public',
      'groups.private': 'Private', 'groups.join': 'Join', 'groups.leave': 'Leave',
      'groups.members': 'Members', 'groups.empty': 'No groups yet', 'groups.posts': 'Posts',
      'groups.owner': 'Owner', 'groups.created': 'Created', 'groups.joined': 'You joined the group',
      'groups.left': 'You left the group', 'groups.new': 'New group', 'groups.about': 'About',

      'settings.title': 'Settings', 'settings.profile': 'Profile', 'settings.appearance': 'Appearance',
      'settings.theme': 'Theme', 'settings.language': 'Language', 'settings.sound': 'Sounds',
      'settings.soundOn': 'Enable sounds', 'settings.volume': 'Volume', 'settings.events': 'Sound events',
      'settings.privacy': 'Privacy', 'settings.hideOnline': 'Hide my online status',
      'settings.save': 'Save', 'settings.saved': 'Saved', 'settings.password': 'Password',
      'settings.oldPassword': 'Current password', 'settings.newPassword': 'New password',
      'settings.changePassword': 'Change password', 'settings.passwordChanged': 'Password changed',
      'settings.logout': 'Sign out', 'settings.avatar': 'Avatar', 'settings.uploadAvatar': 'Upload avatar',
      'settings.avatarHint': 'PNG, JPG, GIF or WebP, up to {0} KB.', 'settings.account': 'Account',
      'settings.danger': 'Danger zone', 'settings.status': 'Status', 'settings.bio': 'About me',
      'settings.website': 'Website', 'settings.email': 'Email', 'settings.birthday': 'Birthday',
      'settings.gender': 'Gender', 'settings.city': 'City', 'settings.country': 'Country',

      'admin.title': 'Administration', 'admin.overview': 'Overview', 'admin.users': 'Users',
      'admin.reports': 'Reports', 'admin.logs': 'Logs', 'admin.broadcast': 'Broadcast',
      'admin.tools': 'Tools', 'admin.stats': 'Statistics', 'admin.online': 'Online',
      'admin.posts': 'Posts', 'admin.comments': 'Comments', 'admin.messages': 'Messages',
      'admin.groups': 'Groups', 'admin.sessions': 'Sessions', 'admin.storage': 'Storage',
      'admin.readOnly': 'READ-ONLY', 'admin.repair': 'Repair storage', 'admin.backup': 'Backup now',
      'admin.reload': 'Reload config', 'admin.ban': 'Ban', 'admin.unban': 'Unban',
      'admin.role': 'Role', 'admin.delete': 'Delete', 'admin.reset': 'Reset password',
      'admin.warn': 'Warn', 'admin.banReason': 'Reason', 'admin.days': 'Days (0 = forever)',
      'admin.confirmDelete': 'Delete this user?', 'admin.broadcastText': 'Message to all users',
      'admin.send': 'Send', 'admin.reportClose': 'Close report', 'admin.note': 'Note',
      'admin.close': 'Close', 'admin.noReports': 'No open reports', 'admin.logLevel': 'Level',
      'admin.refresh': 'Refresh', 'admin.saved': 'Done', 'admin.backupDone': 'Backup created',
      'admin.reloaded': 'Config reloaded', 'admin.repaired': 'Storage repaired',
      'admin.delivered': 'Delivered to {0} users', 'admin.lastSave': 'Last save',
      'admin.saveError': 'Save error', 'admin.uptime': 'Uptime', 'admin.peak': 'Peak online',
      'admin.visitsToday': 'Visits today', 'admin.apiRequests': 'API requests',
      'admin.apiErrors': 'API errors', 'admin.recovery': 'New recovery code',
      'admin.recoveryHint': 'Give this code to the user. It is shown once.',
      'admin.openReports': 'Open reports', 'admin.recentUsers': 'Recent users',
      'admin.onOtherPort': 'The admin board runs on a separate port:', 'admin.disabled': 'The admin board is disabled in the configuration.',
      'admin.open': 'Open the admin board',      'admin.topUsers': 'Most active', 'admin.config': 'Configuration', 'admin.siteName': 'Site name',
      'admin.port': 'Port', 'admin.registration': 'Registration', 'admin.encryption': 'API encryption',
      'admin.captcha': 'Captcha', 'admin.kdf': 'KDF iterations', 'admin.dataDir': 'Data directory',
      'admin.webRoot': 'Web root', 'admin.backupKeep': 'Backups kept',

      'search.title': 'Search', 'search.users': 'People', 'search.posts': 'Posts',
      'search.empty': 'Nothing found', 'search.placeholder': 'Search people and posts...',
      'search.resultsFor': 'Results for "{0}"',

      'common.save': 'Save', 'common.cancel': 'Cancel', 'common.close': 'Close',
      'common.delete': 'Delete', 'common.edit': 'Edit', 'common.confirm': 'Confirm',
      'common.yes': 'Yes', 'common.no': 'No', 'common.loading': 'Loading...',
      'common.error': 'Something went wrong', 'common.retry': 'Retry', 'common.none': 'None',
      'common.all': 'All', 'common.on': 'On', 'common.off': 'Off', 'common.copy': 'Copy',
      'common.copied': 'Copied', 'common.open': 'Open', 'common.back': 'Back',
      'common.more': 'More', 'common.less': 'Less', 'common.unknown': 'Unknown',
      'common.you': 'you', 'common.optional': 'optional', 'common.required': 'required',
      'common.search': 'Search', 'common.create': 'Create', 'common.send': 'Send',
      'common.refresh': 'Refresh', 'common.apply': 'Apply', 'common.reset': 'Reset',

      'time.now': 'now', 'time.minute': '{0}m', 'time.hour': '{0}h', 'time.day': '{0}d',
      'time.ago': '{0} ago', 'time.justNow': 'just now',

      'errors.network': 'Network error. Check your connection.',
      'errors.session': 'Session expired. Please sign in again.',
      'errors.forbidden': 'You do not have permission to do that.',
      'errors.notFound': 'Not found.',
      'errors.rateLimit': 'Slow down a little.',
      'errors.readOnly': 'Storage is read-only. The administrator has been notified.',
      'errors.generic': 'Something went wrong.',

      'sound.click': 'Clicks', 'sound.post': 'New posts', 'sound.like': 'Likes',
      'sound.comment': 'Comments', 'sound.message': 'Messages', 'sound.notification': 'Notifications',
      'sound.friend': 'Friend requests', 'sound.login': 'Sign in', 'sound.logout': 'Sign out',
      'sound.error': 'Errors', 'sound.success': 'Success', 'sound.theme': 'Theme change',

      'theme.light': 'Light', 'theme.dark': 'Dark', 'theme.oled': 'OLED',
      'lang.ru': 'Русский', 'lang.en': 'English', 'lang.uk': 'Українська', 'lang.lt': 'Lietuvių',

      'footer.powered': 'Powered by Social', 'footer.about': 'About', 'footer.source': 'Self-hosted',
      'motd.title': 'Announcement',
      'online.title': 'Online now', 'online.count': '{0} online', 'online.empty': 'Nobody online',
      'stats.title': 'About this server', 'stats.users': 'Users', 'stats.online': 'Online',
      'stats.posts': 'Posts', 'stats.comments': 'Comments', 'stats.likes': 'Likes',
      'stats.visits': 'Visits', 'stats.peak': 'Peak online', 'stats.today': 'Today'
    },

    ru: {
      'app.loading': 'Загрузка...',
      'app.name': 'Социальная сеть',
      'app.tagline': 'Твоя собственная соцсеть',
      'nav.feed': 'Лента', 'nav.friends': 'Друзья', 'nav.messages': 'Сообщения',
      'nav.people': 'Люди', 'nav.groups': 'Группы', 'nav.notifications': 'Уведомления',
      'nav.settings': 'Настройки', 'nav.admin': 'Админка', 'nav.search': 'Поиск',
      'nav.logout': 'Выйти', 'nav.profile': 'Моя страница', 'nav.online': 'Онлайн',
      'nav.menu': 'Меню', 'nav.more': 'Ещё',

      'auth.login': 'Вход', 'auth.register': 'Регистрация', 'auth.recover': 'Восстановление',
      'auth.username': 'Логин', 'auth.password': 'Пароль', 'auth.password2': 'Повторите пароль',
      'auth.displayName': 'Имя', 'auth.captcha': 'Капча', 'auth.captchaHint': 'Введите цифры',
      'auth.invite': 'Код приглашения', 'auth.signIn': 'Войти', 'auth.createAccount': 'Создать аккаунт',
      'auth.noAccount': 'Ещё нет аккаунта?', 'auth.haveAccount': 'Уже зарегистрированы?',
      'auth.forgot': 'Забыли пароль?', 'auth.recoveryCode': 'Код восстановления',
      'auth.recoveryHint': 'Код, который вы сохранили при регистрации.',
      'auth.newPassword': 'Новый пароль', 'auth.reset': 'Задать новый пароль',
      'auth.welcomeBack': 'С возвращением', 'auth.join': 'Присоединяйтесь',
      'auth.terms': 'Я согласен вести себя по-человечески', 'auth.firstAdmin': 'Первый аккаунт становится администратором.',
      'auth.registrationClosed': 'Регистрация закрыта.',
      'auth.loginTitle': 'Вход', 'auth.registerTitle': 'Создание аккаунта', 'auth.recoverTitle': 'Восстановление доступа',
      'auth.loginSub': 'Логин и пароль.', 'auth.registerSub': 'Минута — и вы внутри.',
      'auth.recoverSub': 'Введите код восстановления из регистрации.',
      'auth.saveRecovery': 'Сохраните код восстановления', 'auth.recoverySaved': 'Он показывается только один раз. Спрячьте его в надёжное место.',
      'auth.copy': 'Копировать', 'auth.copied': 'Скопировано',
      'auth.passwordMismatch': 'Пароли не совпадают', 'auth.passwordShort': 'Пароль слишком короткий (минимум 8)',
      'auth.usernameHint': '3-24 символа: буквы, цифры, _ или .', 'auth.captchaWrong': 'Неверный код капчи',
      'auth.badCredentials': 'Неверный логин или пароль', 'auth.locked': 'Слишком много попыток. Попробуйте позже.',
      'auth.banned': 'Аккаунт заблокирован',

      'feed.title': 'Лента', 'feed.home': 'Моя лента', 'feed.global': 'Всё', 'feed.friends': 'Только друзья',
      'feed.composerPlaceholder': 'Что у вас нового?', 'feed.post': 'Опубликовать', 'feed.posting': 'Публикуем...',
      'feed.photo': 'Фото', 'feed.removePhoto': 'Убрать фото', 'feed.visibility': 'Кто увидит',
      'feed.public': 'Все', 'feed.friendsOnly': 'Друзья', 'feed.private': 'Только я',
      'feed.empty': 'Здесь пока пусто', 'feed.emptyHint': 'Добавьте друга или напишите первый пост.',
      'feed.loadMore': 'Показать ещё', 'feed.noMore': 'Это всё', 'feed.like': 'Нравится', 'feed.liked': 'Нравится',
      'feed.comment': 'Комментировать', 'feed.comments': 'Комментарии', 'feed.share': 'Поделиться', 'feed.copiedLink': 'Ссылка скопирована',
      'feed.delete': 'Удалить', 'feed.deleteConfirm': 'Удалить этот пост?', 'feed.deleted': 'Пост удалён',
      'feed.justNow': 'только что', 'feed.edited': 'изменено', 'feed.writeOnWall': 'Написать на стене',
      'feed.posted': 'Опубликовано', 'feed.commentPosted': 'Комментарий добавлен',

      'post.title': 'Пост', 'post.notFound': 'Пост не найден', 'post.commentPlaceholder': 'Написать комментарий...',
      'post.send': 'Отправить', 'post.commentsCount': '{0} комментариев',

      'profile.edit': 'Редактировать', 'profile.message': 'Написать', 'profile.addFriend': 'Добавить в друзья',
      'profile.cancelRequest': 'Отменить заявку', 'profile.accept': 'Принять', 'profile.decline': 'Отклонить',
      'profile.removeFriend': 'Удалить из друзей', 'profile.block': 'Заблокировать', 'profile.unblock': 'Разблокировать',
      'profile.friends': 'Друзья', 'profile.posts': 'Посты', 'profile.likes': 'Лайки',
      'profile.joined': 'Регистрация', 'profile.city': 'Город', 'profile.country': 'Страна',
      'profile.birthday': 'День рождения', 'profile.gender': 'Пол', 'profile.website': 'Сайт',
      'profile.about': 'О себе', 'profile.wall': 'Стена', 'profile.groups': 'Группы',
      'profile.writeOnWall': 'Написать на стене', 'profile.private': 'Эта страница закрыта',
      'profile.online': 'в сети', 'profile.offline': 'не в сети', 'profile.male': 'Мужской',
      'profile.female': 'Женский', 'profile.other': 'Другой', 'profile.since': 'На сайте с',
      'profile.saved': 'Профиль сохранён', 'profile.requestSent': 'Заявка отправлена',

      'friends.title': 'Друзья', 'friends.all': 'Все друзья', 'friends.incoming': 'Входящие заявки',
      'friends.outgoing': 'Отправленные заявки', 'friends.empty': 'Друзей пока нет',
      'friends.emptyHint': 'Найдите людей и отправьте заявку.', 'friends.requestSent': 'Заявка отправлена',
      'friends.accepted': 'Заявка принята', 'friends.removed': 'Удалено из друзей',
      'friends.declined': 'Заявка отклонена', 'friends.blocked': 'Пользователь заблокирован',

      'people.title': 'Люди', 'people.search': 'Поиск людей', 'people.empty': 'Никого не нашли',
      'people.loadMore': 'Показать ещё', 'people.total': 'Человек: {0}',

      'messages.title': 'Сообщения', 'messages.empty': 'Диалогов пока нет', 'messages.select': 'Выберите диалог',
      'messages.placeholder': 'Написать сообщение...', 'messages.send': 'Отправить', 'messages.unread': '{0} непрочитанных',
      'messages.noThreads': 'Сообщений пока нет', 'messages.sent': 'Сообщение отправлено',

      'notifications.title': 'Уведомления', 'notifications.empty': 'Уведомлений нет',
      'notifications.markAll': 'Прочитать все', 'notifications.marked': 'Всё прочитано',
      'notifications.new': 'новых',

      'groups.title': 'Группы', 'groups.create': 'Создать группу', 'groups.name': 'Название',
      'groups.description': 'Описание', 'groups.visibility': 'Видимость', 'groups.public': 'Открытая',
      'groups.private': 'Закрытая', 'groups.join': 'Вступить', 'groups.leave': 'Выйти',
      'groups.members': 'Участники', 'groups.empty': 'Групп пока нет', 'groups.posts': 'Записи',
      'groups.owner': 'Владелец', 'groups.created': 'Создана', 'groups.joined': 'Вы вступили в группу',
      'groups.left': 'Вы вышли из группы', 'groups.new': 'Новая группа', 'groups.about': 'О группе',

      'settings.title': 'Настройки', 'settings.profile': 'Профиль', 'settings.appearance': 'Внешний вид',
      'settings.theme': 'Тема', 'settings.language': 'Язык', 'settings.sound': 'Звуки',
      'settings.soundOn': 'Включить звуки', 'settings.volume': 'Громкость', 'settings.events': 'События',
      'settings.privacy': 'Приватность', 'settings.hideOnline': 'Скрывать статус «в сети»',
      'settings.save': 'Сохранить', 'settings.saved': 'Сохранено', 'settings.password': 'Пароль',
      'settings.oldPassword': 'Текущий пароль', 'settings.newPassword': 'Новый пароль',
      'settings.changePassword': 'Сменить пароль', 'settings.passwordChanged': 'Пароль изменён',
      'settings.logout': 'Выйти из аккаунта', 'settings.avatar': 'Аватар', 'settings.uploadAvatar': 'Загрузить аватар',
      'settings.avatarHint': 'PNG, JPG, GIF или WebP, до {0} КБ.', 'settings.account': 'Аккаунт',
      'settings.danger': 'Опасная зона', 'settings.status': 'Статус', 'settings.bio': 'О себе',
      'settings.website': 'Сайт', 'settings.email': 'E-mail', 'settings.birthday': 'День рождения',
      'settings.gender': 'Пол', 'settings.city': 'Город', 'settings.country': 'Страна',

      'admin.title': 'Администрирование', 'admin.overview': 'Обзор', 'admin.users': 'Пользователи',
      'admin.reports': 'Жалобы', 'admin.logs': 'Логи', 'admin.broadcast': 'Рассылка',
      'admin.tools': 'Инструменты', 'admin.stats': 'Статистика', 'admin.online': 'Онлайн',
      'admin.posts': 'Посты', 'admin.comments': 'Комментарии', 'admin.messages': 'Сообщения',
      'admin.groups': 'Группы', 'admin.sessions': 'Сессии', 'admin.storage': 'Хранилище',
      'admin.readOnly': 'ТОЛЬКО ЧТЕНИЕ', 'admin.repair': 'Починить хранилище', 'admin.backup': 'Сделать бэкап',
      'admin.reload': 'Перечитать конфиг', 'admin.ban': 'Забанить', 'admin.unban': 'Разбанить',
      'admin.role': 'Роль', 'admin.delete': 'Удалить', 'admin.reset': 'Сбросить пароль',
      'admin.warn': 'Предупредить', 'admin.banReason': 'Причина', 'admin.days': 'Дней (0 = навсегда)',
      'admin.confirmDelete': 'Удалить пользователя?', 'admin.broadcastText': 'Сообщение всем',
      'admin.send': 'Отправить', 'admin.reportClose': 'Закрыть жалобу', 'admin.note': 'Заметка',
      'admin.close': 'Закрыть', 'admin.noReports': 'Открытых жалоб нет', 'admin.logLevel': 'Уровень',
      'admin.refresh': 'Обновить', 'admin.saved': 'Готово', 'admin.backupDone': 'Бэкап создан',
      'admin.reloaded': 'Конфиг перечитан', 'admin.repaired': 'Хранилище проверено',
      'admin.delivered': 'Доставлено пользователям: {0}', 'admin.lastSave': 'Последнее сохранение',
      'admin.saveError': 'Ошибка сохранения', 'admin.uptime': 'Время работы', 'admin.peak': 'Пик онлайна',
      'admin.visitsToday': 'Визитов сегодня', 'admin.apiRequests': 'Запросов API',
      'admin.apiErrors': 'Ошибок API', 'admin.recovery': 'Новый код восстановления',
      'admin.recoveryHint': 'Передайте код пользователю. Он показывается один раз.',
      'admin.openReports': 'Открытые жалобы', 'admin.recentUsers': 'Новые пользователи',
      'admin.onOtherPort': 'Админка работает на отдельном порту:', 'admin.disabled': 'Админка отключена в конфигурации.',
      'admin.open': 'Открыть админку',
      'admin.topUsers': 'Самые активные', 'admin.config': 'Конфигурация', 'admin.siteName': 'Название',
      'admin.port': 'Порт', 'admin.registration': 'Регистрация', 'admin.encryption': 'Шифрование API',
      'admin.captcha': 'Капча', 'admin.kdf': 'Итераций KDF', 'admin.dataDir': 'Папка данных',
      'admin.webRoot': 'Корень сайта', 'admin.backupKeep': 'Хранится бэкапов',

      'search.title': 'Поиск', 'search.users': 'Люди', 'search.posts': 'Посты',
      'search.empty': 'Ничего не найдено', 'search.placeholder': 'Поиск людей и постов...',
      'search.resultsFor': 'Результаты по запросу «{0}»',

      'common.save': 'Сохранить', 'common.cancel': 'Отмена', 'common.close': 'Закрыть',
      'common.delete': 'Удалить', 'common.edit': 'Изменить', 'common.confirm': 'Подтвердить',
      'common.yes': 'Да', 'common.no': 'Нет', 'common.loading': 'Загрузка...',
      'common.error': 'Что-то пошло не так', 'common.retry': 'Повторить', 'common.none': 'Нет',
      'common.all': 'Все', 'common.on': 'Вкл', 'common.off': 'Выкл', 'common.copy': 'Копировать',
      'common.copied': 'Скопировано', 'common.open': 'Открыть', 'common.back': 'Назад',
      'common.more': 'Ещё', 'common.less': 'Меньше', 'common.unknown': 'Неизвестно',
      'common.you': 'вы', 'common.optional': 'необязательно', 'common.required': 'обязательно',
      'common.search': 'Найти', 'common.create': 'Создать', 'common.send': 'Отправить',
      'common.refresh': 'Обновить', 'common.apply': 'Применить', 'common.reset': 'Сбросить',

      'time.now': 'сейчас', 'time.minute': '{0} мин', 'time.hour': '{0} ч', 'time.day': '{0} д',
      'time.ago': '{0} назад', 'time.justNow': 'только что',

      'errors.network': 'Ошибка сети. Проверьте соединение.',
      'errors.session': 'Сессия истекла. Войдите снова.',
      'errors.forbidden': 'Недостаточно прав.',
      'errors.notFound': 'Не найдено.',
      'errors.rateLimit': 'Помедленнее.',
      'errors.readOnly': 'Хранилище доступно только для чтения. Администратор уведомлён.',
      'errors.generic': 'Что-то пошло не так.',

      'sound.click': 'Клики', 'sound.post': 'Новые посты', 'sound.like': 'Лайки',
      'sound.comment': 'Комментарии', 'sound.message': 'Сообщения', 'sound.notification': 'Уведомления',
      'sound.friend': 'Заявки в друзья', 'sound.login': 'Вход', 'sound.logout': 'Выход',
      'sound.error': 'Ошибки', 'sound.success': 'Успех', 'sound.theme': 'Смена темы',

      'theme.light': 'Светлая', 'theme.dark': 'Тёмная', 'theme.oled': 'OLED',
      'lang.ru': 'Русский', 'lang.en': 'English', 'lang.uk': 'Українська', 'lang.lt': 'Lietuvių',

      'footer.powered': 'Работает на Social', 'footer.about': 'О сервере', 'footer.source': 'Свой хостинг',
      'motd.title': 'Объявление',
      'online.title': 'Сейчас в сети', 'online.count': 'В сети: {0}', 'online.empty': 'Никого нет',
      'stats.title': 'О сервере', 'stats.users': 'Пользователи', 'stats.online': 'Онлайн',
      'stats.posts': 'Посты', 'stats.comments': 'Комментарии', 'stats.likes': 'Лайки',
      'stats.visits': 'Визиты', 'stats.peak': 'Пик онлайна', 'stats.today': 'Сегодня'
    },

    uk: {
      'nav.feed': 'Стрічка', 'nav.friends': 'Друзі', 'nav.messages': 'Повідомлення',
      'nav.people': 'Люди', 'nav.groups': 'Групи', 'nav.notifications': 'Сповіщення',
      'nav.settings': 'Налаштування', 'nav.admin': 'Адмінка', 'nav.search': 'Пошук', 'nav.logout': 'Вийти',
      'auth.login': 'Вхід', 'auth.register': 'Реєстрація', 'auth.username': 'Логін', 'auth.password': 'Пароль',
      'auth.signIn': 'Увійти', 'auth.createAccount': 'Створити акаунт', 'auth.forgot': 'Забули пароль?',
      'feed.title': 'Стрічка', 'feed.home': 'Моя стрічка', 'feed.global': 'Усі', 'feed.post': 'Опублікувати',
      'feed.composerPlaceholder': 'Що нового?', 'feed.like': 'Подобається', 'feed.comment': 'Коментувати',
      'profile.edit': 'Редагувати', 'profile.message': 'Написати', 'profile.addFriend': 'Додати в друзі',
      'friends.title': 'Друзі', 'people.title': 'Люди', 'messages.title': 'Повідомлення',
      'notifications.title': 'Сповіщення', 'groups.title': 'Групи', 'settings.title': 'Налаштування',
      'common.save': 'Зберегти', 'common.cancel': 'Скасувати', 'common.delete': 'Видалити',
      'common.loading': 'Завантаження...', 'common.search': 'Пошук', 'common.send': 'Надіслати'
    },

    lt: {
      'nav.feed': 'Srautas', 'nav.friends': 'Draugai', 'nav.messages': 'Žinutės',
      'nav.people': 'Žmonės', 'nav.groups': 'Grupės', 'nav.notifications': 'Pranešimai',
      'nav.settings': 'Nustatymai', 'nav.admin': 'Admin', 'nav.search': 'Paieška', 'nav.logout': 'Atsijungti',
      'auth.login': 'Prisijungti', 'auth.register': 'Registracija', 'auth.username': 'Vartotojas',
      'auth.password': 'Slaptažodis', 'auth.signIn': 'Prisijungti', 'auth.createAccount': 'Sukurti paskyrą',
      'auth.forgot': 'Pamiršote slaptažodį?', 'feed.title': 'Srautas', 'feed.home': 'Mano srautas',
      'feed.global': 'Visi', 'feed.post': 'Skelbti', 'feed.composerPlaceholder': 'Kas naujo?',
      'feed.like': 'Patinka', 'feed.comment': 'Komentuoti', 'profile.edit': 'Redaguoti',
      'profile.message': 'Rašyti', 'profile.addFriend': 'Pridėti draugą', 'friends.title': 'Draugai',
      'people.title': 'Žmonės', 'messages.title': 'Žinutės', 'notifications.title': 'Pranešimai',
      'groups.title': 'Grupės', 'settings.title': 'Nustatymai', 'common.save': 'Išsaugoti',
      'common.cancel': 'Atšaukti', 'common.delete': 'Ištrinti', 'common.loading': 'Kraunama...',
      'common.search': 'Ieškoti', 'common.send': 'Siųsti'
    }
  };

  var LANGS = ['ru', 'en', 'uk', 'lt'];
  var lang = 'ru';

  function detect(defaultLang) {
    try {
      var saved = localStorage.getItem('social.lang');
      if (saved && DICT[saved]) return saved;
    } catch (e) { }
    var nav = navigator.languages && navigator.languages.length ? navigator.languages : [navigator.language || 'en'];
    for (var i = 0; i < nav.length; i++) {
      var code = String(nav[i] || '').toLowerCase();
      if (code.indexOf('ru') === 0) return 'ru';
      if (code.indexOf('uk') === 0) return 'uk';
      if (code.indexOf('lt') === 0) return 'lt';
      if (code.indexOf('en') === 0) return 'en';
    }
    return defaultLang && DICT[defaultLang] ? defaultLang : 'en';
  }

  function t(key, a, b, c) {
    var s = (DICT[lang] && DICT[lang][key]) || DICT.en[key] || DICT.ru[key] || key;
    if (a !== undefined) s = s.replace(/\{0\}/g, String(a));
    if (b !== undefined) s = s.replace(/\{1\}/g, String(b));
    if (c !== undefined) s = s.replace(/\{2\}/g, String(c));
    return s;
  }

  function setLang(code) {
    if (!DICT[code]) return lang;
    lang = code;
    try { localStorage.setItem('social.lang', code); } catch (e) { }
    try { document.documentElement.lang = code; } catch (e) { }
    return lang;
  }

  function getLang() { return lang; }

  function apply(root) {
    var nodes = (root || document).querySelectorAll('[data-i18n]');
    for (var i = 0; i < nodes.length; i++) {
      var key = nodes[i].getAttribute('data-i18n');
      if (key) nodes[i].textContent = t(key);
    }
    var ph = (root || document).querySelectorAll('[data-i18n-ph]');
    for (var j = 0; j < ph.length; j++) {
      var k = ph[j].getAttribute('data-i18n-ph');
      if (k) ph[j].setAttribute('placeholder', t(k));
    }
  }

  function labels() {
    var out = [];
    for (var i = 0; i < LANGS.length; i++) out.push({ code: LANGS[i], name: t('lang.' + LANGS[i]) });
    return out;
  }

  return {
    t: t,
    setLang: setLang,
    getLang: getLang,
    detect: detect,
    apply: apply,
    labels: labels,
    langs: LANGS,
    dict: DICT
  };
})();
