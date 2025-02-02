﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;

using Newtonsoft.Json;

using FlyleafLib.MediaFramework.MediaStream;

namespace FlyleafLib.Plugins
{
    // TODO: Config + Interrupt for WebClient / HttpClient
    public class OpenSubtitlesOrg : PluginBase, ISearchOnlineSubtitles, IDownloadSubtitles
    {
        public new int  Priority            { get; set; } = 2000;
        public int      SearchTimeoutMs     { get; set; } = 15000;
        public int      DownloadTimeoutMs   { get; set; } = 30000;

        static Dictionary<string, List<OpenSubtitlesOrgJson>> cache = new Dictionary<string, List<OpenSubtitlesOrgJson>>();
        static readonly string restUrl = "https://rest.opensubtitles.org/search";
        static readonly string userAgent = "Flyleaf v2.0";

        public override void OnInitializingSwitch()
        {
            OnInitialized();
        }

        public bool DownloadSubtitles(ExternalSubtitlesStream extStream)
        {
            if (GetTag(extStream) == null || !(GetTag(extStream) is OpenSubtitlesOrgJson))
                return false;

            var sub = (OpenSubtitlesOrgJson)GetTag(extStream);

            try
            {
                string subDownloadLinkEnc = sub.SubDownloadLink;

                string filename = sub.SubFileName;
                if (filename.EndsWith(".srt", StringComparison.OrdinalIgnoreCase))
                    filename = filename.Substring(0, filename.Length - 4);

                string zipPath = Path.GetTempPath() + filename + "." + sub.SubLanguageID + ".srt.gz";

                File.Delete(zipPath);
                File.Delete(zipPath.Substring(0, zipPath.Length - 3));

                Log.Debug($"Downloading {zipPath}");
                Utils.DownloadFile(subDownloadLinkEnc, zipPath, DownloadTimeoutMs);

                Log.Debug($"Unzipping {zipPath}");
                string unzipPath = Utils.GZipDecompress(zipPath);
                Log.Debug($"Unzipped at {unzipPath}");

                sub.AvailableAt = unzipPath;
            }
            catch (Exception e)
            {
                sub.AvailableAt = null;
                Log.Debug($"Failed to download subtitle {sub.SubFileName} - {e.Message}");
                return false;
            }

            extStream.Url = sub.AvailableAt;
            return true;
        }

        public void SearchOnlineSubtitles()
        {
            string hash = null;
            if (Selected.FileSize != 0)
                if (Selected.IOStream != null)
                {
                    lock (decoder.VideoDemuxer.lockFmtCtx) // fmtCtx reads the same IOStream / need to ensure that we don't change position
                    {
                        var savePos = Selected.IOStream.Position;
                        hash = Utils.ToHexadecimal(ComputeMovieHash(Selected.IOStream, Selected.FileSize));
                        Selected.IOStream.Position = savePos;
                    }
                }
                else
                    hash = Utils.ToHexadecimal(ComputeMovieHash(Selected.Url));

            Search(Selected.Title, hash, Selected.FileSize, Config.Subtitles.Languages);
        }

        // https://trac.OpenSubtitlesJson.org/projects/OpenSubtitlesJson/wiki/HashSourceCodes
        public byte[] ComputeMovieHash(string filename)
        {
            byte[] result;
            using (Stream input = File.OpenRead(filename))
            {
                result = ComputeMovieHash(input);
            }
            return result;
        }
        public byte[] ComputeMovieHash(Stream input, long length = -1)
        {
            long lhash, streamsize;
            streamsize = input.Length;
            lhash = length == -1 ? streamsize : length;

            long i = 0;
            byte[] buffer = new byte[sizeof(long)];
            while (i < 65536 / sizeof(long) && (input.Read(buffer, 0, sizeof(long)) > 0))
            {
                i++;
                lhash += BitConverter.ToInt64(buffer, 0);
            }

            input.Position = Math.Max(0, streamsize - 65536);
            i = 0;
            while (i < 65536 / sizeof(long) && (input.Read(buffer, 0, sizeof(long)) > 0))
            {
                i++;
                lhash += BitConverter.ToInt64(buffer, 0);
            }

            byte[] result = BitConverter.GetBytes(lhash);
            Array.Reverse(result);
            return result;
        }

        public List<OpenSubtitlesOrgJson> SearchByHash(string hash, long length)
        {
            List<OpenSubtitlesOrgJson> subsCopy = new List<OpenSubtitlesOrgJson>();

            if (string.IsNullOrEmpty(hash)) return subsCopy;

            if (cache.ContainsKey(hash + "|" + length))
            {
                foreach (OpenSubtitlesOrgJson sub in cache[hash + "|" + length]) subsCopy.Add(sub);

                return subsCopy;
            }

            using (HttpClient client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(SearchTimeoutMs) })
            {
                string resp = "";
                List<OpenSubtitlesOrgJson> subs = new List<OpenSubtitlesOrgJson>();

                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Add("X-User-Agent", userAgent);

                try
                {
                    Log.Debug($"Searching for /moviebytesize-{length}/moviehash-{hash}");
                    resp = client.PostAsync($"{restUrl}/moviebytesize-{length}/moviehash-{hash}", null).Result.Content.ReadAsStringAsync().Result;
                    subs = JsonConvert.DeserializeObject<List<OpenSubtitlesOrgJson>>(resp);
                    Log.Debug($"Search Results {subs.Count}");
                    cache.Add(hash + "|" + length, subs);
                    foreach (OpenSubtitlesOrgJson sub in subs) subsCopy.Add(sub);
                }
                catch (Exception e) { Log.Debug($"Error fetching subtitles {e.Message} - {e.StackTrace}"); }

                return subsCopy;
            }
        }
        public List<OpenSubtitlesOrgJson> SearchByIMDB(string imdbid, Language lang = null, string season = null, string episode = null)
        {
            if (lang == null) lang = Language.Get("eng");
            List<OpenSubtitlesOrgJson> subsCopy = new List<OpenSubtitlesOrgJson>();

            if (cache.ContainsKey(imdbid + "|" + season + "|" + episode + lang.IdSubLanguage))
            {
                foreach (OpenSubtitlesOrgJson sub in cache[imdbid + "|" + season + "|" + episode + lang.IdSubLanguage]) subsCopy.Add(sub);

                return subsCopy;
            }

            using (HttpClient client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(SearchTimeoutMs) })
            {
                string resp = "";
                List<OpenSubtitlesOrgJson> subs = new List<OpenSubtitlesOrgJson>();

                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Add("X-User-Agent", userAgent);

                try
                {
                    string qSeason = season != null ? $"/season-{season}" : "";
                    string qEpisode = episode != null ? $"/episode-{episode}" : "";
                    string query = $"{qEpisode}/imdbid-{imdbid}{qSeason}/sublanguageid-{lang.IdSubLanguage}";

                    Log.Debug($"Searching for {query}");
                    resp = client.PostAsync($"{restUrl}{query}", null).Result.Content.ReadAsStringAsync().Result;
                    subs = JsonConvert.DeserializeObject<List<OpenSubtitlesOrgJson>>(resp);
                    Log.Debug($"Search Results {subs.Count}");

                    cache.Add(imdbid + "|" + season + "|" + episode + lang.IdSubLanguage, subs);
                    foreach (OpenSubtitlesOrgJson sub in subs) subsCopy.Add(sub);
                }
                catch (Exception e) { Log.Debug($"Error fetching subtitles {e.Message} - {e.StackTrace}"); }

                return subsCopy;
            }
        }
        public List<OpenSubtitlesOrgJson> SearchByName(string name, Language lang = null)
        {
            if (lang == null) lang = Language.Get("eng");
            List<OpenSubtitlesOrgJson> subsCopy = new List<OpenSubtitlesOrgJson>();

            if (cache.ContainsKey(name + "|" + lang.IdSubLanguage))
            {
                foreach (OpenSubtitlesOrgJson sub in cache[name + "|" + lang.IdSubLanguage]) subsCopy.Add(sub);

                return subsCopy;
            }

            using (HttpClient client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(SearchTimeoutMs) })
            {
                string resp = "";
                List<OpenSubtitlesOrgJson> subs = new List<OpenSubtitlesOrgJson>();

                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Add("X-User-Agent", userAgent);

                try
                {
                    Log.Debug($"Searching for /query-{Uri.EscapeDataString(name.Replace('.', ' '))}/sublanguageid-{lang.IdSubLanguage}");
                    resp = client.PostAsync($"{restUrl}/query-{Uri.EscapeDataString(name.Replace('.', ' '))}/sublanguageid-{lang.IdSubLanguage}", null).Result.Content.ReadAsStringAsync().Result;
                    subs = JsonConvert.DeserializeObject<List<OpenSubtitlesOrgJson>>(resp);
                    Log.Debug($"Search Results {subs.Count}");

                    cache.Add(name + "|" + lang.IdSubLanguage, subs);
                    foreach (OpenSubtitlesOrgJson sub in subs) subsCopy.Add(sub);
                }
                catch (Exception e) { Log.Debug($"Error fetching subtitles {e.Message} - {e.StackTrace}"); }

                return subsCopy;
            }
        }
        public void Search(string filename, string hash, long length, List<Language> Languages)
        {
            List<OpenSubtitlesOrgJson> subs = SearchByHash(hash, length);

            bool imdbExists = subs != null && subs.Count > 0 && subs[0].IDMovieImdb != null && subs[0].IDMovieImdb.Trim() != "";
            bool isEpisode = imdbExists && subs[0].SeriesSeason != null && subs[0].SeriesSeason.Trim() != "" && subs[0].SeriesSeason.Trim() != "0" && subs[0].SeriesEpisode != null && subs[0].SeriesEpisode.Trim() != "" && subs[0].SeriesEpisode.Trim() != "0";

            foreach (Language lang in Languages)
            {
                if (imdbExists)
                {
                    if (isEpisode)
                        subs.AddRange(SearchByIMDB(subs[0].IDMovieImdb, lang, subs[0].SeriesSeason, subs[0].SeriesEpisode));
                    else
                        subs.AddRange(SearchByIMDB(subs[0].IDMovieImdb, lang));
                }

                subs.AddRange(SearchByName(filename, lang));
            }

            // Unique by SubHashes (if any)
            List<OpenSubtitlesOrgJson> uniqueList = new List<OpenSubtitlesOrgJson>();
            List<int> removeIds = new List<int>();
            for (int i = 0; i < subs.Count - 1; i++)
            {
                if (removeIds.Contains(i)) continue;

                for (int l = i + 1; l < subs.Count; l++)
                {
                    if (removeIds.Contains(l)) continue;

                    if (subs[l].SubHash == subs[i].SubHash)
                    {
                        if (subs[l].AvailableAt == null)
                            removeIds.Add(l);
                        else
                        { removeIds.Add(i); break; }
                    }
                }
            }
            for (int i = 0; i < subs.Count; i++)
                if (!removeIds.Contains(i)) uniqueList.Add(subs[i]);

            subs.Clear();
            foreach (Language lang in Languages)
            {
                IEnumerable<OpenSubtitlesOrgJson> movieHashRating =
                    from sub in uniqueList
                    where sub.ISO639 != null && sub.ISO639 == lang.ISO639 && sub.MatchedBy.ToLower() == "moviehash"
                    orderby float.Parse(sub.SubRating) descending
                    select sub;

                IEnumerable<OpenSubtitlesOrgJson> rating =
                    from sub in uniqueList
                    where sub.ISO639 != null && sub.ISO639 == lang.ISO639 && sub.MatchedBy.ToLower() != "moviehash"
                    orderby float.Parse(sub.SubRating) descending
                    select sub;

                foreach (var t1 in movieHashRating) subs.Add(t1);
                foreach (var t1 in rating) subs.Add(t1);
            }

            foreach (var sub in subs)
            {
                float rating = float.Parse(sub.SubRating);
                if (rating > 10) rating = 10;
                else if (rating < 0) rating = 0;

                AddExternalStream(new ExternalSubtitlesStream()
                {
                    Title = sub.SubFileName,
                    Rating = rating,
                    Language = Language.Get(sub.ISO639)
                }, sub);
            }
                
        }
    }
}