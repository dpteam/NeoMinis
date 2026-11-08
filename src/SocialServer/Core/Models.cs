using System;
using System.Collections.Generic;

namespace Social.Core
{
    public sealed class User
    {
        public long Id;
        public string Username = "";
        public string DisplayName = "";
        public string Verifier = "";
        public string Salt = "";
        public string Recovery = "";
        public string RecoverySalt = "";
        public string Role = "user";
        public DateTime Created = DateTime.UtcNow;
        public DateTime LastSeen = DateTime.UtcNow;
        public DateTime LastLogin = DateTime.UtcNow;
        public string Status = "";
        public string Bio = "";
        public string City = "";
        public string Country = "";
        public string Birthday = "";
        public string Gender = "";
        public string Website = "";
        public string Avatar = "";
        public string Theme = "";
        public string Lang = "";
        public bool Sound = true;
        public string Privacy = "public";
        public bool HideOnline;
        public bool Banned;
        public string BanReason = "";
        public DateTime BannedUntil = DateTime.MinValue;
        public int Warnings;
        public int Karma;
        public string Email = "";
        public string IpHash = "";
        public int LoginCount;
        public bool Deleted;
        public DateTime LastPostAt = DateTime.MinValue;
        public DateTime LastCommentAt = DateTime.MinValue;
        public DateTime LastMessageAt = DateTime.MinValue;

        // profile extras (DeadVK had games/movies/music/books, a persona and a mood)
        public string FavGames = "";
        public string FavMovies = "";
        public string FavMusic = "";
        public string FavBooks = "";
        public string Persona = "";
        public string Mood = "";            // happy | sad | "" (legacy mood files)
        public string Badges = "";          // comma separated badge ids
        public string NotifyMute = "";      // comma separated notification types the user muted
        public string LegacySource = "";    // e.g. "deadvk" for imported accounts
        public string LegacyId = "";        // id in the old database

        public bool IsAdmin { get { return Role == "admin"; } }
        public bool IsModerator { get { return Role == "admin" || Role == "moderator"; } }
        public bool IsBannedNow { get { return Banned && (BannedUntil == DateTime.MinValue || BannedUntil > DateTime.UtcNow); } }
    }

    public sealed class Post
    {
        public long Id;
        public long Author;
        public long Wall;          // 0 = author's own wall, otherwise the profile owner
        public long Group;         // 0 = normal post
        public string Body = "";
        public string Image = "";
        public string Video = "";  // external video link (DeadVK had a video column)
        public DateTime Created = DateTime.UtcNow;
        public DateTime Edited = DateTime.MinValue;
        public string Visibility = "public";
        public List<long> Likes = new List<long>();
        public int Comments;
        public bool Deleted;
        public bool Pinned;
        public string LegacySource = "";
        public string LegacyId = "";
    }

    public sealed class Bookmark
    {
        public long User;
        public long Post;
        public DateTime Created = DateTime.UtcNow;
    }

    public sealed class Comment
    {
        public long Id;
        public long Post;
        public long Author;
        public string Body = "";
        public DateTime Created = DateTime.UtcNow;
        public bool Deleted;
    }

    public sealed class FriendLink
    {
        public long From;
        public long To;
        public string Status = "pending"; // pending | accepted | declined | blocked
        public DateTime Created = DateTime.UtcNow;
        public DateTime Updated = DateTime.UtcNow;
    }

    public sealed class Message
    {
        public long Id;
        public long From;
        public long To;
        public string Body = "";
        public DateTime Created = DateTime.UtcNow;
        public bool Read;
        public bool DeletedByFrom;
        public bool DeletedByTo;
    }

    public sealed class Notification
    {
        public long Id;
        public long User;
        public long Actor;
        public string Type = "system";
        public string Text = "";
        public string Link = "";
        public DateTime Created = DateTime.UtcNow;
        public bool Read;
    }

    public sealed class Group
    {
        public long Id;
        public string Name = "";
        public string Slug = "";
        public string Description = "";
        public string Avatar = "";
        public long Owner;
        public List<long> Members = new List<long>();
        public DateTime Created = DateTime.UtcNow;
        public string Visibility = "public";
        public bool Deleted;
    }

    public sealed class Report
    {
        public long Id;
        public string Type = "post";
        public long Target;
        public long Reporter;
        public string Reason = "";
        public DateTime Created = DateTime.UtcNow;
        public string Status = "open"; // open | closed
        public string Note = "";
        public long HandledBy;
        public DateTime HandledAt = DateTime.MinValue;
    }

    public sealed class Meta
    {
        public int Version = 1;
        public DateTime Created = DateTime.UtcNow;
        public long NextUserId = 1;
        public long NextPostId = 1;
        public long NextCommentId = 1;
        public long NextMessageId = 1;
        public long NextNotificationId = 1;
        public long NextGroupId = 1;
        public long NextReportId = 1;
        public long TotalRegistrations;
        public DateTime LastBackup = DateTime.MinValue;
        public string InstallId = "";
    }
}
