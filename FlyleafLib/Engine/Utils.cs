﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

using Microsoft.Win32;

namespace FlyleafLib
{
    public unsafe static class Utils
    {
        #region MediaEngine
        private static int uniqueId;
        public static int GetUniqueId() { Interlocked.Increment(ref uniqueId); return uniqueId; }

        /// <summary>
        /// Begin invokes the UI thread if required to execute the specified action
        /// </summary>
        /// <param name="action"></param>
        public static void UI(Action action)
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(action, System.Windows.Threading.DispatcherPriority.DataBind);
        }

        /// <summary>
        /// Invokes the UI thread if required to execute the specified action
        /// </summary>
        /// <param name="action"></param>
        public static void UIInvoke(Action action)
        {
            // NOTE: Deadlocks will happen if we call this from a thread that we wait for it with EnsureThreadDone from an UI thread
            System.Windows.Application.Current.Dispatcher.Invoke(action);
        }

        public static int Align(int num, int align)
        {
            int mod = num % align;
            if (mod == 0)
                return num;

            return num + (align - num % align);
        }

        public static float Scale(float value, float inMin, float inMax, float outMin, float outMax)
        {
            return (value - inMin) * (outMax - outMin) / (inMax - inMin) + outMin;
        }

        /// <summary>
        /// Adds a windows firewall rule if not already exists for the specified program path
        /// </summary>
        /// <param name="ruleName">Default value is Flyleaf</param>
        /// <param name="path">Default value is current executable path</param>
        public static void AddFirewallRule(string ruleName = null, string path = null)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    if (string.IsNullOrEmpty(ruleName))
                        ruleName = "Flyleaf";

                    if (string.IsNullOrEmpty(path))
                        path = Process.GetCurrentProcess().MainModule.FileName;

                    path = $"\"{path}\"";

                    // Check if rule already exists
                    Process proc = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName        = "cmd",
                            Arguments       = $"/C netsh advfirewall firewall show rule name={ruleName} verbose | findstr /L {path}",
                            CreateNoWindow  = true,
                            UseShellExecute = false,
                            RedirectStandardOutput 
                                            = true,
                            WindowStyle     = ProcessWindowStyle.Hidden
                        }
                    };

                    proc.Start();
                    proc.WaitForExit();

                    if (proc.StandardOutput.Read() > 0)
                        return;

                    // Add rule with admin rights
                    proc = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName        = "cmd",
                            Arguments       = $"/C netsh advfirewall firewall add rule name={ruleName} dir=in  action=allow enable=yes program={path} profile=any &" +
                                                 $"netsh advfirewall firewall add rule name={ruleName} dir=out action=allow enable=yes program={path} profile=any",
                            Verb            = "runas",
                            CreateNoWindow  = true,
                            UseShellExecute = true,
                            WindowStyle     = ProcessWindowStyle.Hidden
                        }
                    };

                    proc.Start();
                    proc.WaitForExit();

                    Log($"Firewall rule \"{ruleName}\" added for {path}");
                } catch { }
            });
        }

        // We can't trust those
        //public static private bool    IsDesignMode=> (bool) DesignerProperties.IsInDesignModeProperty.GetMetadata(typeof(DependencyObject)).DefaultValue;
        //public static bool            IsDesignMode    = LicenseManager.UsageMode == LicenseUsageMode.Designtime; // Will not work properly (need to be called from non-static class constructor)

        //public static bool          IsWin11         = Regex.IsMatch(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName", "").ToString(), "Windows 11");
        //public static bool          IsWin10         = Regex.IsMatch(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName", "").ToString(), "Windows 10");
        //public static bool          IsWin8          = Regex.IsMatch(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName", "").ToString(), "Windows 8");
        //public static bool          IsWin7          = Regex.IsMatch(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName", "").ToString(), "Windows 7");

        public static List<string>  MovieExts       = new List<string>() { "mp4", "m4v", "m4e", "mkv", "mpg", "mpeg" , "mpv", "mp4p", "mpe" , "m1v", "m2ts", "m2p", "m2v", "movhd", "moov", "movie", "movx", "mjp", "mjpeg", "mjpg", "amv" , "asf", "m4v", "3gp", "ogm", "ogg", "vob", "ts", "rm", "3gp", "3gp2", "3gpp", "3g2", "f4v", "f4a", "f4p", "f4b", "mts", "m2ts", "gifv", "avi", "mov", "flv", "wmv", "qt", "avchd", "swf", "cam", "nsv", "ram", "rm", "x264", "xvid", "wmx", "wvx", "wx", "video", "viv", "vivo", "vid", "dat", "bik", "bix", "dmf", "divx" };
        public static List<string>  SubsExts        = new List<string>() { "srt", "txt", "sub", "ssa", "ass" };

        public static List<string> GetMoviesSorted(List<string> movies)
        {
            List<string> moviesSorted = new List<string>();

            for (int i=0; i<movies.Count; i++)
            {
                string ext = Path.GetExtension(movies[i]);
                if (ext == null || ext.Trim() == "") continue;

                if (MovieExts.Contains(ext.Substring(1,ext.Length-1))) moviesSorted.Add(movies[i]);
            }

            moviesSorted.Sort(new NaturalStringComparer());

            return moviesSorted;
        }
        public sealed class NaturalStringComparer : IComparer<string> { public int Compare(string a, string b) { return NativeMethods.StrCmpLogicalW(a, b); } }

        public static string GetRecInnerException(Exception e)
        {
            string dump = "";
            var cur = e.InnerException;

            for (int i=0; i<4; i++)
            {
                if (cur == null) break;
                dump += "\r\n - " + cur.Message;
                cur = cur.InnerException;
            }

            return dump;
        }
        public static string GetUrlExtention(string url) { return url.LastIndexOf(".") > 0 ? url.Substring(url.LastIndexOf(".") + 1).ToLower() : ""; }
        public static List<Language> GetSystemLanguages()
        {
            List<Language>  Languages  = new List<Language>();
            Language        systemLang = Language.Get(CultureInfo.CurrentCulture.TwoLetterISOLanguageName);
            if (systemLang.LanguageName != "English") Languages.Add(systemLang);

            foreach (System.Windows.Forms.InputLanguage lang in System.Windows.Forms.InputLanguage.InstalledInputLanguages)
                if (Language.Get(lang.Culture.TwoLetterISOLanguageName).ISO639 != systemLang.ISO639 && Language.Get(lang.Culture.TwoLetterISOLanguageName).LanguageName != "English") 
                    Languages.Add(Language.Get(lang.Culture.TwoLetterISOLanguageName));

            Languages.Add(Language.Get("English"));

            return Languages;
        }

        public static bool ExtractSeasonEpisode(string text, out int season, out int episode)
        {
            // Other possibilities "S01 Episode 03", "01x03"

            var res = Regex.Match(text, @"(^|[^a-z0-9])s(?<season>[0-9]{1,2})e(?<episode>[0-9]{1,2})($|[^a-z0-9])", RegexOptions.IgnoreCase);

            if (res.Groups["season"].Value != "" && res.Groups["episode"].Value != "")
            {
                season = int.Parse(res.Groups["season"].Value);
                episode = int.Parse(res.Groups["episode"].Value);

                return true;
            }

            res = Regex.Match(text, @"(^|[^a-z0-9])Season[^a-z0-9]+(?<season>[0-9]{1,2}).*Episode[^a-z0-9]+(?<episode>[0-9]{1,2})($|[^a-z0-9])", RegexOptions.IgnoreCase);
            
            if (res.Groups["season"].Value != "" && res.Groups["episode"].Value != "")
            {
                season = int.Parse(res.Groups["season"].Value);
                episode = int.Parse(res.Groups["episode"].Value);

                return true;
            }

            res = Regex.Match(text, @"(^|[^a-z0-9])(?<season>[0-9]{1,2})x(?<episode>[0-9]{1,2})($|[^a-z0-9])", RegexOptions.IgnoreCase);
            
            if (res.Groups["season"].Value != "" && res.Groups["episode"].Value != "")
            {
                season = int.Parse(res.Groups["season"].Value);
                episode = int.Parse(res.Groups["episode"].Value);

                return true;
            }

            season = -1;
            episode = -1;

            return false;
        }

        public static string FindNextAvailableFile(string fileName)
        {
            if (!File.Exists(fileName)) return fileName;

            string tmp = Path.Combine(Path.GetDirectoryName(fileName),Regex.Replace(Path.GetFileNameWithoutExtension(fileName), @"(.*) (\([0-9]+)\)$", "$1"));
            string newName;

            for (int i=1; i<101; i++)
            {
                newName = tmp  + " (" + i + ")" + Path.GetExtension(fileName);
                if (!File.Exists(newName)) return newName;
            }

            return null;
        }
        public static string GetValidFileName(string name)  { return string.Join("_", name.Split(Path.GetInvalidFileNameChars())); }

        public static string FindFileBelow(string filename)
        {
            string current = Directory.GetCurrentDirectory();

            while (current != null)
            {
                if (File.Exists(Path.Combine(current, filename)))
                    return Path.Combine(current, filename);

                current = Directory.GetParent(current)?.FullName;
            }

            return null;
        }
        public static string GetFolderPath(string folder)
        {
            if (folder.StartsWith(":"))
            {
                folder = folder.Substring(1);
                return FindFolderBelow(folder);
            }

            if (Path.IsPathRooted(folder))
                return folder;

            return Path.GetFullPath(folder);
        }

        public static string FindFolderBelow(string folder)
        {
            string current = Directory.GetCurrentDirectory();

            while (current != null)
            {
                if (Directory.Exists(Path.Combine(current, folder)))
                    return Path.Combine(current, folder);

                current = Directory.GetParent(current)?.FullName;
            }

            return null;
        }
        public static string GetUserDownloadPath() { try { return Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders\").GetValue("{374DE290-123F-4565-9164-39C4925E467B}").ToString(); } catch (Exception) { return null; } }
        public static string DownloadToString(string url, int timeoutMs = 30000)
        {
            try
            {
                using (HttpClient client = new HttpClient() { Timeout = TimeSpan.FromMilliseconds(timeoutMs) })
                return client.GetAsync(url).Result.Content.ReadAsStringAsync().Result;
            } catch (Exception e)
            {
                Log($"Download failed {e.Message} [Url: {(url != null ? url : "Null")}]");
            }
            
            return null;
        }
        public static bool DownloadFile(string url, string filename, int timeoutMs = 30000, bool overwrite = true)
        {
            try
            {
                using (HttpClient client = new HttpClient() { Timeout = TimeSpan.FromMilliseconds(timeoutMs) })
                {
                    using (FileStream fs = new FileStream(filename, overwrite ? FileMode.Create : FileMode.CreateNew))
                        client.GetAsync(url).Result.Content.CopyToAsync(fs).Wait();

                    return true;
                }
            } catch (Exception e)
            {
                Log($"Download failed {e.Message} [Url: {(url != null ? url : "Null")}, Path: {(filename != null ? filename : "Null")}]");
            }

            return false;
        }
        public static string FixFileUrl(string url)
        {
            try
            {
                if (url == null || url.Length < 5)
                    return url;

                if (url.Substring(0, 5).ToLower() == "file:")
                    return (new Uri(url)).LocalPath;
            } catch { }

            return url;
        }

        static List<PerformanceCounter> gpuCounters;

        public static void GetGPUCounters()
        {
            var category        = new PerformanceCounterCategory("GPU Engine");
            var counterNames    = category.GetInstanceNames();
            gpuCounters         = new List<PerformanceCounter>();

            foreach (string counterName in counterNames)
                if (counterName.EndsWith("engtype_3D"))
                    foreach (PerformanceCounter counter in category.GetCounters(counterName))
                        if (counter.CounterName == "Utilization Percentage")
                            gpuCounters.Add(counter);
        }
        public static float GetGPUUsage()
        {
            float result = 0f;

            try
            {
                if (gpuCounters == null) GetGPUCounters();

                gpuCounters.ForEach(x => { _ = x.NextValue(); });
                Thread.Sleep(1000);
                gpuCounters.ForEach(x => { result += x.NextValue(); });

            } catch (Exception e) { Log($"[GPUUsage] Error {e.Message}"); result = -1f; GetGPUCounters(); }

            return result;
        }
        public static string GZipDecompress(string filename)
        {
            string newFileName = "";

            FileInfo fileToDecompress = new FileInfo(filename);
            using (FileStream originalFileStream = fileToDecompress.OpenRead())
            {
                string currentFileName = fileToDecompress.FullName;
                newFileName = currentFileName.Remove(currentFileName.Length - fileToDecompress.Extension.Length);

                using (FileStream decompressedFileStream = File.Create(newFileName))
                {
                    using (GZipStream decompressionStream = new GZipStream(originalFileStream, CompressionMode.Decompress))
                    {
                        decompressionStream.CopyTo(decompressedFileStream);
                    }
                }
            }

            return newFileName;
        }

        public unsafe static string BytePtrToStringUTF8(byte* bytePtr)
        {
            if (bytePtr == null) return null;
            if (*bytePtr == 0) return string.Empty;

            var byteBuffer = new List<byte>(1024);
            var currentByte = default(byte);

            while (true)
            {
                currentByte = *bytePtr;
                if (currentByte == 0)
                    break;

                byteBuffer.Add(currentByte);
                bytePtr++;
            }

            return Encoding.UTF8.GetString(byteBuffer.ToArray());
        }
        
        public static System.Windows.Media.Color WinFormsToWPFColor(System.Drawing.Color sColor) { return System.Windows.Media.Color.FromArgb(sColor.A, sColor.R, sColor.G, sColor.B); }
        public static System.Drawing.Color WPFToWinFormsColor(System.Windows.Media.Color wColor) { return System.Drawing.Color.FromArgb(wColor.A, wColor.R, wColor.G, wColor.B); }

        public static System.Windows.Media.Color VorticeToWPFColor(Vortice.Mathematics.Color sColor) { return System.Windows.Media.Color.FromArgb(sColor.A, sColor.R, sColor.G, sColor.B); }
        public static Vortice.Mathematics.Color WPFToVorticeColor(System.Windows.Media.Color wColor) { return new Vortice.Mathematics.Color(wColor.R, wColor.G, wColor.B, wColor.A); }


        public static string ToHexadecimal(byte[] bytes)
        {
            StringBuilder hexBuilder = new StringBuilder();
            for(int i = 0; i < bytes.Length; i++)
            {
                hexBuilder.Append(bytes[i].ToString("x2"));
            }
            return hexBuilder.ToString();
        }
        public static int GCD(int a, int b) { return b == 0 ? a : GCD(b, a % b); }
        public static string TicksToTime(long ticks) { return new TimeSpan(ticks).ToString(); }
        public static void Log(string msg) { try { Debug.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] {msg}"); } catch (Exception) { Debug.WriteLine($"[............] [MediaFramework] {msg}"); } } // System.ArgumentOutOfRangeException ???
        #endregion

        public static class NativeMethods
        {
            [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
            public static extern int StrCmpLogicalW(string psz1, string psz2);

            [DllImport("user32.dll")]
            public static extern int SetWindowLong (IntPtr hWnd, int nIndex, uint dwNewLong);

            [DllImport("user32.dll",SetLastError = true)]
            public static extern bool GetWindowInfo(IntPtr hwnd, ref WINDOWINFO pwi);
            [DllImport("user32.dll")]
            public static extern int ShowCursor(bool bShow);

            [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod", SetLastError = true)]
            public static extern uint TimeBeginPeriod(uint uMilliseconds);

            [DllImport("winmm.dll", EntryPoint = "timeEndPeriod", SetLastError = true)]
            public static extern uint TimeEndPeriod(uint uMilliseconds);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto,SetLastError = true)]
            public static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);
            [FlagsAttribute]
            public enum EXECUTION_STATE :uint
            {
                ES_AWAYMODE_REQUIRED    = 0x00000040,
                ES_CONTINUOUS           = 0x80000000,
                ES_DISPLAY_REQUIRED     = 0x00000002,
                ES_SYSTEM_REQUIRED      = 0x00000001
            }

            [DllImport("user32.dll")]
            public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

            public delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

            [DllImport("user32.dll")]
            public static extern bool GetWindowRect(IntPtr hwnd, ref RECT rectangle);

            [StructLayout(LayoutKind.Sequential)]
            public struct WINDOWINFO
            {
                public uint cbSize;
                public RECT rcWindow;
                public RECT rcClient;
                public uint dwStyle;
                public uint dwExStyle;
                public uint dwWindowStatus;
                public uint cxWindowBorders;
                public uint cyWindowBorders;
                public ushort atomWindowType;
                public ushort wCreatorVersion;

                public WINDOWINFO(Boolean? filler) : this()   // Allows automatic initialization of "cbSize" with "new WINDOWINFO(null/true/false)".
                {
                    cbSize = (UInt32)(Marshal.SizeOf(typeof( WINDOWINFO )));
                }

            }
            public struct RECT
            {
                public int Left     { get; set; }
                public int Top      { get; set; }
                public int Right    { get; set; }
                public int Bottom   { get; set; }
            }
        }
    }

    [Serializable] // https://scatteredcode.net/c-serializable-dictionary/
    public class SerializableDictionary<TKey, TVal> : Dictionary<TKey, TVal>, IXmlSerializable, ISerializable, INotifyPropertyChanged, INotifyCollectionChanged
    {
        #region Private Properties
        protected XmlSerializer ValueSerializer
        {
            get { return _valueSerializer ?? (_valueSerializer = new XmlSerializer(typeof(TVal))); }
        }
        private XmlSerializer KeySerializer
        {
            get { return _keySerializer ?? (_keySerializer = new XmlSerializer(typeof(TKey))); }
        }
        #endregion
        #region Private Members
        private XmlSerializer _keySerializer;
        private XmlSerializer _valueSerializer;
        #endregion
        #region Constructors
        public SerializableDictionary()
        {
        }
        public SerializableDictionary(IDictionary<TKey, TVal> dictionary) : base(dictionary) { }
        public SerializableDictionary(IEqualityComparer<TKey> comparer) : base(comparer) { }
        public SerializableDictionary(int capacity) : base(capacity) { }
        public SerializableDictionary(IDictionary<TKey, TVal> dictionary, IEqualityComparer<TKey> comparer)
          : base(dictionary, comparer) { }
        public SerializableDictionary(int capacity, IEqualityComparer<TKey> comparer)
          : base(capacity, comparer) { }
        #endregion
        #region ISerializable Members
        protected SerializableDictionary(SerializationInfo info, StreamingContext context)
        {
            int itemCount = info.GetInt32("itemsCount");
            for (int i = 0; i < itemCount; i++)
            {
                KeyValuePair<TKey, TVal> kvp = (KeyValuePair<TKey, TVal>)info.GetValue(String.Format(CultureInfo.InvariantCulture, "Item{0}", i), typeof(KeyValuePair<TKey, TVal>));
                Add(kvp.Key, kvp.Value);
            }
        }
        void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue("itemsCount", Count);
            int itemIdx = 0; foreach (KeyValuePair<TKey, TVal> kvp in this)
            {
                info.AddValue(String.Format(CultureInfo.InvariantCulture, "Item{0}", itemIdx), kvp, typeof(KeyValuePair<TKey, TVal>));
                itemIdx++;
            }
        }
        #endregion
        #region IXmlSerializable Members
        void IXmlSerializable.WriteXml(XmlWriter writer)
        {
            foreach (KeyValuePair<TKey, TVal> kvp in this)
            {
                writer.WriteStartElement("item");
                writer.WriteStartElement("key");
                KeySerializer.Serialize(writer, kvp.Key);
                writer.WriteEndElement();
                writer.WriteStartElement("value");
                ValueSerializer.Serialize(writer, kvp.Value);
                writer.WriteEndElement();
                writer.WriteEndElement();
            }
        }
        void IXmlSerializable.ReadXml(XmlReader reader)
        {
            if (reader.IsEmptyElement)
            {
                return;
            }
            // Move past container
            if (reader.NodeType == XmlNodeType.Element && !reader.Read())
                throw new XmlException("Error in De serialization of SerializableDictionary");
            while (reader.NodeType != XmlNodeType.EndElement)
            {
                reader.ReadStartElement("item");
                reader.ReadStartElement("key");
                TKey key = (TKey)KeySerializer.Deserialize(reader);
                reader.ReadEndElement();
                reader.ReadStartElement("value");
                TVal value = (TVal)ValueSerializer.Deserialize(reader);
                reader.ReadEndElement();
                reader.ReadEndElement();
                Add(key, value);
                reader.MoveToContent();
            }
            // Move past container
            if (reader.NodeType == XmlNodeType.EndElement)
            {
                reader.ReadEndElement();
            }
            else
            {
                throw new XmlException("Error in Deserialization of SerializableDictionary");
            }
        }
        XmlSchema IXmlSerializable.GetSchema()
        {
            return null;
        }
        #endregion


        public event PropertyChangedEventHandler PropertyChanged;
        public event NotifyCollectionChangedEventHandler CollectionChanged;

        public new TVal this[TKey key]
        {
            get => base[key];
            
            set
            {
                if (ContainsKey(key) && base[key].Equals(value)) return;

                if (CollectionChanged != null)
                {
                    KeyValuePair<TKey, TVal> oldItem = new KeyValuePair<TKey, TVal>(key, base[key]);
                    KeyValuePair<TKey, TVal> newItem = new KeyValuePair<TKey, TVal>(key, value);
                    base[key] = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(key.ToString()));
                    CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, newItem, oldItem, this.ToList().IndexOf(newItem)));
                }
                else
                {
                    base[key] = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(key.ToString()));
                }
            }
        }
    }
}