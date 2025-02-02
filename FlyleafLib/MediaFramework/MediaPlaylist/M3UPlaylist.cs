﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace FlyleafLib.MediaFramework.MediaPlaylist
{
    public class M3UPlaylistItem
    {
        public long     Duration    { get; set; }
        public string   Title       { get; set; }
        public string   Url         { get; set; }
        public string   UserAgent   { get; set; }
        public string   Referrer    { get; set; }
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();
    }

    public class M3UPlaylist
    {

        public static List<M3UPlaylistItem> ParseFromHttp(string url, int timeoutMs = 30000)
        {
            string downStr = Utils.DownloadToString(url, timeoutMs);
            if (downStr == null)
                return null;

            using (StringReader reader = new StringReader(downStr))
                return Parse(reader);
        }

        public static List<M3UPlaylistItem> ParseFromString(string text)
        {
            using (StringReader reader = new StringReader(text))
                return Parse(reader);
        }

        public static List<M3UPlaylistItem> Parse(string filename)
        {
            using (StreamReader reader = new StreamReader(filename))
                return Parse(reader);
        }
        private static List<M3UPlaylistItem> Parse(TextReader reader)
        {
            string line;
            List<M3UPlaylistItem> items = new List<M3UPlaylistItem>();

            while ((line = reader.ReadLine()) != null)
            {
                if (line.StartsWith("#EXTINF"))
                {
                    M3UPlaylistItem item = new M3UPlaylistItem();
                    MatchCollection matches = Regex.Matches(line, " ([^\\s=]+)=\"([^\\s\"]+)\"");
                    foreach (Match match in matches)
                        if (match.Groups.Count > 3)
                            item.Tags.Add(match.Groups[1].Value, match.Groups[2].Value);

                    item.Title = GetMatch(line, @",\s*([^=,]+)$");

                    while ((line = reader.ReadLine()) != null && line.StartsWith("#EXTVLCOPT"))
                    {
                        if (item.UserAgent == null)
                        {
                            item.UserAgent = GetMatch(line, "http-user-agent\\s*=\\s*\"*(.*)\"*");
                            if (item.UserAgent != null) continue;
                        }

                        if (item.Referrer == null)
                        {
                            item.Referrer = GetMatch(line, "http-referrer\\s*=\\s*\"*(.*)\"*");
                            if (item.Referrer != null) continue;
                        }
                    }

                    item.Url = line;
                    items.Add(item);
                }
            }

            return items;
        }

        private static string GetMatch(string text, string pattern)
        {
            Match match = Regex.Match(text, pattern);
            if (match.Success && match.Groups.Count > 1)
                return match.Groups[1].Value;

            return null;
        }
    }
}
