using System.IO;
using UnityEngine;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
#endif

// 棋譜・局面の JSON ファイルの保存と選択（Prototype の downloadKifu / pickKifuFile）。
//  ・Unity エディター … EditorUtility.SaveFilePanel / OpenFilePanel
//  ・Windows ビルド   … Windows のファイルの保存・選択ダイアログ（comdlg32 の GetSaveFileNameW / GetOpenFileNameW）
//  ・そのほか         … ダイアログはまだない。保存は Application.persistentDataPath/Kifu に書き、選択はそのフォルダの一番新しい .json を読む
// 最後に使ったフォルダを覚えておく（PlayerPrefs）。ダイアログが開いている間、ゲームは止まる
public static class KifuFileDialog
{
    const string LastDirKey = "UltimateShogi.KifuDir";

    // 保存する（defaultName を初期値にして保存先を選ぶ）。保存したパスを返す。キャンセルなら null（error も null）、失敗なら null と理由
    public static string SaveJson(string title, string defaultName, string contents, out string error)
    {
        error = null;
        string path;
#if UNITY_EDITOR
        path = UnityEditor.EditorUtility.SaveFilePanel(title, LastDir(), defaultName, "json");
#elif UNITY_STANDALONE_WIN
        path = WindowsDialog.Save(title, LastDir(), defaultName);
#else
        path = Path.Combine(FallbackDir(), defaultName);
#endif
        if (string.IsNullOrEmpty(path)) return null;
        if (!path.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase)) path += ".json";
        try
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, contents, new System.Text.UTF8Encoding(false));
        }
        catch (System.Exception e)
        {
            error = $"保存できませんでした：{e.Message}";
            return null;
        }
        Remember(path);
        Debug.Log($"[Kifu] 保存した：{path}");
        return path;
    }

    // JSON ファイルを選んで中身を返す。キャンセルなら null（error も null）、読めなければ null と理由
    public static string OpenJson(string title, out string error)
    {
        error = null;
        string path;
#if UNITY_EDITOR
        path = UnityEditor.EditorUtility.OpenFilePanel(title, LastDir(), "json");
#elif UNITY_STANDALONE_WIN
        path = WindowsDialog.Open(title, LastDir());
#else
        path = NewestInFallbackDir();
        if (path == null)
        {
            error = $"ファイルの選択はこの環境ではまだ使えません（{FallbackDir()} に .json を置くと、一番新しいものを読みます）";
            return null;
        }
#endif
        if (string.IsNullOrEmpty(path)) return null;
        try
        {
            string text = File.ReadAllText(path, System.Text.Encoding.UTF8);
            Remember(path);
            Debug.Log($"[Kifu] 読み込んだ：{path}");
            return text;
        }
        catch (System.Exception e)
        {
            error = $"読み込めませんでした：{e.Message}";
            return null;
        }
    }

    static string LastDir()
    {
        string dir = PlayerPrefs.GetString(LastDirKey, "");
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
        string docs = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
        return !string.IsNullOrEmpty(docs) && Directory.Exists(docs) ? docs : Application.persistentDataPath;
    }

    static void Remember(string path)
    {
        string dir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir)) return;
        PlayerPrefs.SetString(LastDirKey, dir);
        PlayerPrefs.Save();
    }

    static string FallbackDir()
    {
        return Path.Combine(Application.persistentDataPath, "Kifu");
    }

    static string NewestInFallbackDir()
    {
        string dir = FallbackDir();
        if (!Directory.Exists(dir)) return null;
        string newest = null;
        System.DateTime newestTime = System.DateTime.MinValue;
        foreach (string f in Directory.GetFiles(dir, "*.json"))
        {
            System.DateTime t = File.GetLastWriteTimeUtc(f);
            if (newest == null || t > newestTime)
            {
                newest = f;
                newestTime = t;
            }
        }
        return newest;
    }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    // Windows のファイルダイアログ（comdlg32）。OPENFILENAMEW をそのまま渡す
    static class WindowsDialog
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        class OpenFileName
        {
            public int lStructSize = Marshal.SizeOf(typeof(OpenFileName));
            public IntPtr hwndOwner = IntPtr.Zero;
            public IntPtr hInstance = IntPtr.Zero;
            public string lpstrFilter = null;
            public string lpstrCustomFilter = null;
            public int nMaxCustFilter = 0;
            public int nFilterIndex = 1;
            public IntPtr lpstrFile = IntPtr.Zero;
            public int nMaxFile = 0;
            public string lpstrFileTitle = null;
            public int nMaxFileTitle = 0;
            public string lpstrInitialDir = null;
            public string lpstrTitle = null;
            public int Flags = 0;
            public short nFileOffset = 0;
            public short nFileExtension = 0;
            public string lpstrDefExt = null;
            public IntPtr lCustData = IntPtr.Zero;
            public IntPtr lpfnHook = IntPtr.Zero;
            public string lpTemplateName = null;
            public IntPtr pvReserved = IntPtr.Zero;
            public int dwReserved = 0;
            public int FlagsEx = 0;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool GetOpenFileNameW([In, Out] OpenFileName ofn);

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool GetSaveFileNameW([In, Out] OpenFileName ofn);

        [DllImport("user32.dll")]
        static extern IntPtr GetActiveWindow();

        const int OFN_OVERWRITEPROMPT = 0x00000002;
        const int OFN_NOCHANGEDIR = 0x00000008;
        const int OFN_PATHMUSTEXIST = 0x00000800;
        const int OFN_FILEMUSTEXIST = 0x00001000;
        const int OFN_EXPLORER = 0x00080000;
        const int MaxPath = 4096;
        const string Filter = "JSON ファイル (*.json)\0*.json\0すべてのファイル (*.*)\0*.*\0\0";

        public static string Open(string title, string initialDir)
        {
            return Run(title, initialDir, "", false);
        }

        public static string Save(string title, string initialDir, string defaultName)
        {
            return Run(title, initialDir, defaultName, true);
        }

        static string Run(string title, string initialDir, string fileName, bool save)
        {
            IntPtr buffer = Marshal.AllocHGlobal(MaxPath * 2);
            try
            {
                // 初期のファイル名を書き込む（終端の 0 まで）
                char[] chars = new char[MaxPath];
                if (!string.IsNullOrEmpty(fileName)) fileName.CopyTo(0, chars, 0, Math.Min(fileName.Length, MaxPath - 1));
                Marshal.Copy(chars, 0, buffer, MaxPath);

                OpenFileName ofn = new OpenFileName
                {
                    hwndOwner = GetActiveWindow(),
                    lpstrFilter = Filter,
                    lpstrFile = buffer,
                    nMaxFile = MaxPath,
                    lpstrInitialDir = initialDir,
                    lpstrTitle = title,
                    lpstrDefExt = "json",
                    Flags = OFN_EXPLORER | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | (save ? OFN_OVERWRITEPROMPT : OFN_FILEMUSTEXIST),
                };
                bool ok = save ? GetSaveFileNameW(ofn) : GetOpenFileNameW(ofn);
                if (!ok) return null; // キャンセル（またはエラー）
                return Marshal.PtrToStringUni(buffer);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Kifu] ファイルダイアログを開けませんでした：{e.Message}");
                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }
#endif
}
