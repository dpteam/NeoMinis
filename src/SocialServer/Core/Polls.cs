using System;
using System.Collections.Generic;

namespace Social.Core
{
    /// <summary>
    /// Polls attached to a post. Kept as two newline separated strings on the post
    /// so the JSON tables stay flat: options one per line, and for each option a
    /// line with the voter ids. Single choice, one vote per account, a second click
    /// on the same option takes the vote back.
    /// </summary>
    public static class Polls
    {
        public const int MaxOptions = 6;
        public const int MaxOptionLength = 80;
        public const int MaxQuestionLength = 160;

        public static bool Has(Post p)
        {
            return p != null && !string.IsNullOrEmpty(p.PollQuestion) && Options(p).Count >= 2;
        }

        public static List<string> Options(Post p)
        {
            List<string> list = new List<string>();
            if (p == null || string.IsNullOrEmpty(p.PollOptions)) return list;
            string[] parts = p.PollOptions.Split('\n');
            for (int i = 0; i < parts.Length; i++)
            {
                string o = parts[i].Trim();
                if (o.Length > 0) list.Add(o);
            }
            return list;
        }

        /// <summary>Voter ids per option; the list always matches Options().</summary>
        public static List<List<long>> Votes(Post p)
        {
            List<List<long>> list = new List<List<long>>();
            int count = Options(p).Count;
            string[] lines = string.IsNullOrEmpty(p == null ? null : p.PollVotes)
                ? new string[0] : p.PollVotes.Split('\n');
            for (int i = 0; i < count; i++)
            {
                List<long> voters = new List<long>();
                if (i < lines.Length && lines[i].Trim().Length > 0)
                {
                    string[] ids = lines[i].Split(',');
                    for (int k = 0; k < ids.Length; k++)
                    {
                        long id;
                        if (long.TryParse(ids[k].Trim(), out id) && id > 0) voters.Add(id);
                    }
                }
                list.Add(voters);
            }
            return list;
        }

        public static void StoreVotes(Post p, List<List<long>> votes)
        {
            if (p == null) return;
            List<string> lines = new List<string>();
            for (int i = 0; i < votes.Count; i++)
            {
                List<string> ids = new List<string>();
                for (int k = 0; k < votes[i].Count; k++) ids.Add(votes[i][k].ToString());
                lines.Add(string.Join(",", ids.ToArray()));
            }
            p.PollVotes = string.Join("\n", lines.ToArray());
        }

        public static void Set(Post p, string question, List<string> options)
        {
            if (p == null) return;
            p.PollQuestion = Util.SingleLine(question ?? "", MaxQuestionLength);
            List<string> clean = new List<string>();
            if (options != null)
            {
                for (int i = 0; i < options.Count && clean.Count < MaxOptions; i++)
                {
                    string o = Util.SingleLine(options[i] ?? "", MaxOptionLength);
                    if (o.Length > 0) clean.Add(o);
                }
            }
            p.PollOptions = string.Join("\n", clean.ToArray());
            p.PollVotes = "";
        }

        public static int IndexOfVote(Post p, long userId)
        {
            List<List<long>> votes = Votes(p);
            for (int i = 0; i < votes.Count; i++) if (votes[i].Contains(userId)) return i;
            return -1;
        }

        public static int Total(Post p)
        {
            List<List<long>> votes = Votes(p);
            int n = 0;
            for (int i = 0; i < votes.Count; i++) n += votes[i].Count;
            return n;
        }

        /// <summary>
        /// Records a vote. Returns the option the user now backs, or -1 when the
        /// click took the previous vote back.
        /// </summary>
        public static int Vote(Post p, long userId, int option)
        {
            List<string> options = Options(p);
            if (option < 0 || option >= options.Count) return IndexOfVote(p, userId);
            List<List<long>> votes = Votes(p);
            int current = -1;
            for (int i = 0; i < votes.Count; i++)
            {
                if (votes[i].Contains(userId)) { current = i; votes[i].Remove(userId); }
            }
            if (current == option) { StoreVotes(p, votes); return -1; }
            votes[option].Add(userId);
            StoreVotes(p, votes);
            return option;
        }

        public static void Clear(Post p)
        {
            if (p == null) return;
            p.PollQuestion = "";
            p.PollOptions = "";
            p.PollVotes = "";
        }
    }
}