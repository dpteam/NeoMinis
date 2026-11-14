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

        // community moderation: a post waiting for an editor to approve it
        public bool Pending;

        // poll attached to the post (see Core/Polls.cs)
        public string PollQuestion = "";
        public string PollOptions = "";   // one option per line
        public string PollVotes = "";     // one line per option, voter ids comma separated

        // emoji reactions (see Core/Reactions.cs)
        public string Reactions = "";

        // future publication time; MinValue = published right away
        public DateTime ScheduledFor = DateTime.MinValue;

        // repost: id of the original post (0 = original) and how many times this
        // post has been shared further
        public long RepostOf;
        public int Reposts;
    }

    public sealed class Bookmark
    {
        public long User;
        public long Post;
        public DateTime Created = DateTime.UtcNow;
    }

    /// <summary>
    /// An uploaded track or clip in a user's media library. The file lives under
    /// /media/library/ and the metadata here; nothing else references it.
    /// </summary>
    public sealed class MediaItem
    {
        public long Id;
        public long Owner;
        public string Kind = "audio";   // audio | video | game
        public string Title = "";
        public string Artist = "";
        public string Url = "";
        public long Size;
        public int Duration;            // seconds, 0 when the browser could not tell
        public long SavedFrom;          // owner id of the original when copied from a shelf
        public DateTime Created = DateTime.UtcNow;

        // games only
        public string Description = "";
        public int Plays;
        public List<long> Likes = new List<long>();
    }

    /// <summary>
    /// A named list of library items. Items are media ids in play order; a playlist
    /// never owns the files, so deleting a track simply leaves a gap.
    /// </summary>
    public sealed class Playlist
    {
        public long Id;
        public long Owner;
        public string Name = "";
        public List<long> Items = new List<long>();
        public DateTime Created = DateTime.UtcNow;
        public DateTime Updated = DateTime.UtcNow;
    }

    /// <summary>
    /// Finished call, kept for the history list. Live calls live only in memory.
    /// </summary>
    public sealed class CallRecord
    {
        public long Id;
        public string Kind = "audio";
        public string Room = "";
        public long Initiator;
        public List<long> Participants = new List<long>();
        public DateTime Started = DateTime.UtcNow;
        public DateTime Ended = DateTime.MinValue;
    }

    /// <summary>
    /// An unfinished post. Drafts keep text, video link, visibility, wall/group and
    /// a poll; pictures are deliberately not stored so the JSON tables stay small.
    /// </summary>
    public sealed class Draft
    {
        public long Id;
        public long User;
        public string Body = "";
        public string Video = "";
        public string Visibility = "public";
        public long Wall;
        public long Group;
        public string PollQuestion = "";
        public string PollOptions = "";
        public DateTime Created = DateTime.UtcNow;
        public DateTime Updated = DateTime.UtcNow;
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
        public string Image = "";      // optional picture / animated GIF, /media/messages/...
        public string Images = "";     // album: extra pictures, one URL per line (Image is the first)
        public string Audio = "";      // optional voice message, /media/messages/...
        public int AudioSec;           // duration of the voice message in seconds
        public long ReplyTo;           // id of the quoted message, 0 = none
        public string Reactions = "";  // see Core/Reactions.cs: "emoji|id,id" per line
        public string FileUrl = "";    // generic attachment, /media/files/...
        public string FileName = "";   // original name, shown in the chat
        public long FileSize;          // bytes, for the download hint
        public long ForwardedFrom;     // original author when the message was forwarded, 0 = not forwarded
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

        // communities: who may moderate, who asked to join, and what happens to
        // an ordinary member's post
        public List<long> Editors = new List<long>();
        public List<long> Requests = new List<long>();
        public string PostPolicy = "all";   // all | editors | review
    }

    public sealed class Report
    {        public long Id;
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

    /// <summary>
    /// Anonymous note to the operators: a bug report, an idea or a plain message.
    /// Nothing links it to an account — only an optional contact string the sender
    /// typed themselves, plus a hashed IP for abuse handling.
    /// </summary>
    public sealed class Feedback
    {
        public long Id;
        public string Kind = "bug";      // bug | idea | other
        public string Message = "";
        public string Contact = "";      // optional, free text
        public string Page = "";         // route the sender was on, e.g. "#/im/2"
        public string App = "";          // user agent, truncated
        public string IpHash = "";
        public DateTime Created = DateTime.UtcNow;
        public string Status = "new";    // new | read | closed
        public string Note = "";
        public long HandledBy;
        public DateTime HandledAt = DateTime.MinValue;
    }

    /// <summary>
    /// A gift one person sent another. <see cref="Kind"/> is a key of the
    /// built-in catalogue (<see cref="GiftCatalog"/>), so the tile never depends
    /// on an uploaded file.
    /// </summary>
    public sealed class Gift
    {
        public long Id;
        public long From;
        public long To;
        public string Kind = "heart";
        public string Message = "";
        public bool Anonymous;
        public DateTime Created = DateTime.UtcNow;
        public bool Seen;        // the recipient opened the gifts page
        public bool Hidden;      // the recipient removed it from their wall
    }

    public sealed class Meta
    {
        public int Version = 1;        public DateTime Created = DateTime.UtcNow;
        public long NextUserId = 1;
        public long NextPostId = 1;
        public long NextCommentId = 1;
        public long NextMessageId = 1;
        public long NextNotificationId = 1;
        public long NextGroupId = 1;
        public long NextReportId = 1;
        public long NextDraftId = 1;
        public long NextMediaId = 1;
        public long NextPlaylistId = 1;
        public long NextFeedbackId = 1;
        public long NextCallId = 1;
        public long NextGiftId = 1;
        public long TotalRegistrations;
        public DateTime LastBackup = DateTime.MinValue;
        public string InstallId = "";
    }
}
