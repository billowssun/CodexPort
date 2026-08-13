using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: AssemblyTitle("CodexPort")]
[assembly: AssemblyDescription("Move local Codex chats between Windows computers without copying configuration or credentials.")]
[assembly: AssemblyCompany("Local utility")]
[assembly: AssemblyProduct("CodexPort")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace CodexPort
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly Button exportButton;
        private readonly Button importButton;
        private readonly Button openFolderButton;
        private readonly Label locationLabel;
        private readonly Label stateLabel;
        private readonly ProgressBar progress;
        private readonly TextBox log;
        private readonly string codexHome;

        public MainForm()
        {
            codexHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");

            Text = "CodexPort";
            ClientSize = new Size(720, 520);
            MinimumSize = new Size(680, 500);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = Color.FromArgb(247, 247, 245);

            var title = new Label
            {
                Text = "CodexPort",
                Font = new Font("Microsoft YaHei UI", 20F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(28, 24)
            };

            var subtitle = new Label
            {
                Text = "一键关闭 Codex、迁移聊天并恢复启动；不迁移登录、配置、插件、技能或密钥。",
                AutoSize = true,
                ForeColor = Color.FromArgb(75, 75, 75),
                Location = new Point(31, 70)
            };

            locationLabel = new Label
            {
                Text = "数据位置：" + codexHome,
                AutoSize = true,
                ForeColor = Color.FromArgb(95, 95, 95),
                Location = new Point(31, 102)
            };

            exportButton = CreatePrimaryButton("导出聊天包", new Point(31, 139));
            importButton = CreateSecondaryButton("导入聊天包", new Point(231, 139));
            openFolderButton = CreateSecondaryButton("打开数据目录", new Point(431, 139));
            exportButton.Click += ExportButtonClick;
            importButton.Click += ImportButtonClick;
            openFolderButton.Click += OpenFolderButtonClick;

            stateLabel = new Label
            {
                Text = "就绪",
                AutoSize = true,
                Location = new Point(31, 206),
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
            };

            progress = new ProgressBar
            {
                Location = new Point(31, 235),
                Size = new Size(650, 13),
                Style = ProgressBarStyle.Continuous
            };

            log = new TextBox
            {
                Location = new Point(31, 270),
                Size = new Size(650, 170),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9F)
            };

            var warning = new Label
            {
                Text = "点击导出或导入会自动关闭 Codex，正在运行的任务会被中断。迁移包可能含有隐私或密钥，请安全保管。",
                AutoSize = true,
                MaximumSize = new Size(650, 0),
                ForeColor = Color.FromArgb(145, 75, 20),
                Location = new Point(31, 454)
            };

            Controls.AddRange(new Control[]
            {
                title, subtitle, locationLabel, exportButton, importButton, openFolderButton,
                stateLabel, progress, log, warning
            });

            RefreshSummary();
        }

        private static Button CreatePrimaryButton(string text, Point location)
        {
            return new Button
            {
                Text = text,
                Location = location,
                Size = new Size(174, 46),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(28, 28, 28),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
        }

        private static Button CreateSecondaryButton(string text, Point location)
        {
            return new Button
            {
                Text = text,
                Location = location,
                Size = new Size(174, 46),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(30, 30, 30),
                Cursor = Cursors.Hand
            };
        }

        private void RefreshSummary()
        {
            try
            {
                int active = MigrationEngine.CountJsonl(Path.Combine(codexHome, "sessions"));
                int archived = MigrationEngine.CountJsonl(Path.Combine(codexHome, "archived_sessions"));
                WriteLog("检测到普通聊天 " + active + " 个，已归档聊天 " + archived + " 个。");
            }
            catch (Exception ex)
            {
                WriteLog("读取聊天数量失败：" + ex.Message);
            }
        }

        private void ExportButtonClick(object sender, EventArgs e)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string output = Path.Combine(desktop, "codex-chats-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".codexchat");
            StartWork("正在关闭 Codex 并导出……", delegate
            {
                var result = MigrationEngine.Export(codexHome, output, WriteLogThreadSafe);
                try
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + output + "\"") { UseShellExecute = true });
                }
                catch { }
                return "导出完成，迁移包已保存到桌面。\r\n聊天：" + result.TotalThreads + " 个\r\n文件：" + result.FileCount +
                       " 个\r\nSHA-256：" + result.PackageSha256 + "\r\n\r\n" + output;
            });
        }

        private void ImportButtonClick(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "选择 Codex 聊天迁移包";
                dialog.Filter = "Codex 聊天迁移包 (*.codexchat)|*.codexchat|所有文件 (*.*)|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                string package = dialog.FileName;
                StartWork("正在关闭 Codex、校验并导入……", delegate
                {
                    var result = MigrationEngine.Import(codexHome, package, WriteLogThreadSafe);
                    string launchStatus;
                    try
                    {
                        CodexProcessManager.StartCodex();
                        launchStatus = "Codex 已自动启动。";
                    }
                    catch (Exception ex)
                    {
                        launchStatus = "聊天已成功导入，但自动启动 Codex 失败：" + ex.Message;
                    }
                    return "导入完成\r\n聊天：" + result.TotalThreads + " 个\r\n" + launchStatus +
                           "\r\n\r\n自动备份：\r\n" + result.BackupPath;
                });
            }
        }

        private void OpenFolderButtonClick(object sender, EventArgs e)
        {
            if (!Directory.Exists(codexHome))
            {
                MessageBox.Show(this, "目录尚不存在。请先安装并启动一次 Codex。", "未找到 Codex", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Process.Start("explorer.exe", codexHome);
        }

        private void StartWork(string state, Func<string> action)
        {
            SetBusy(true, state);
            var worker = new BackgroundWorker();
            worker.DoWork += delegate(object sender, DoWorkEventArgs e)
            {
                e.Result = action();
            };
            worker.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs e)
            {
                SetBusy(false, e.Error == null ? "完成" : "操作失败");
                if (e.Error != null)
                {
                    WriteLog("错误：" + e.Error.Message);
                    MessageBox.Show(this, e.Error.Message, "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else
                {
                    WriteLog((string)e.Result);
                    MessageBox.Show(this, (string)e.Result, "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    RefreshSummary();
                }
            };
            worker.RunWorkerAsync();
        }

        private void SetBusy(bool busy, string state)
        {
            exportButton.Enabled = !busy;
            importButton.Enabled = !busy;
            openFolderButton.Enabled = !busy;
            stateLabel.Text = state;
            progress.Style = busy ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
            if (!busy) progress.Value = 0;
        }

        private void WriteLogThreadSafe(string text)
        {
            if (IsDisposed) return;
            BeginInvoke((MethodInvoker)delegate { WriteLog(text); });
        }

        private void WriteLog(string text)
        {
            log.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text + Environment.NewLine);
        }
    }

    internal static class MigrationEngine
    {
        internal static bool TestMode = false;
        private const int FormatVersion = 1;
        private const string ToolVersion = "1.1.0";
        private static readonly string[] HistoryDirectories =
        {
            "sessions", "archived_sessions", "codex-remote-attachments", "generated_images", "visualizations"
        };
        private static readonly string[] HistoryFiles =
        {
            "session_index.jsonl", "state_5.sqlite", "goals_1.sqlite"
        };

        public static ExportResult Export(string codexHome, string outputPath, Action<string> report)
        {
            PrepareCodexStopped(report);
            if (!Directory.Exists(codexHome)) throw new InvalidOperationException("没有找到 Codex 数据目录：" + codexHome);
            if (IsInside(outputPath, codexHome)) throw new InvalidOperationException("迁移包不能保存在 .codex 数据目录内部。");
            if (File.Exists(outputPath)) File.Delete(outputPath);

            string temp = CreateTempDirectory();
            try
            {
                report("正在制作一致性数据库快照……");
                var sources = CollectRegularFiles(codexHome);
                AddDatabaseSnapshot(codexHome, temp, "state_5.sqlite", true, sources);
                AddDatabaseSnapshot(codexHome, temp, "goals_1.sqlite", false, sources);

                int active = CountJsonl(Path.Combine(codexHome, "sessions"));
                int archived = CountJsonl(Path.Combine(codexHome, "archived_sessions"));
                var manifest = new PackageManifest
                {
                    FormatVersion = FormatVersion,
                    ToolVersion = ToolVersion,
                    CreatedUtc = DateTime.UtcNow.ToString("o"),
                    ActiveThreadFiles = active,
                    ArchivedThreadFiles = archived,
                    IncludesConfiguration = false,
                    Files = new List<ManifestFile>()
                };

                report("正在计算校验值并压缩 " + sources.Count + " 个文件……");
                using (var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create))
                {
                    foreach (var source in sources.OrderBy(s => s.EntryPath, StringComparer.OrdinalIgnoreCase))
                    {
                        string hash = ComputeSha256(source.SourcePath);
                        var info = new FileInfo(source.SourcePath);
                        manifest.Files.Add(new ManifestFile { Path = source.EntryPath, Length = info.Length, Sha256 = hash });
                        var entry = archive.CreateEntry(source.EntryPath, CompressionLevel.Optimal);
                        entry.LastWriteTime = SafeZipTime(info.LastWriteTime);
                        using (var input = new FileStream(source.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                        using (var output = entry.Open()) input.CopyTo(output);
                    }

                    var serializer = CreateSerializer();
                    byte[] manifestBytes = Encoding.UTF8.GetBytes(serializer.Serialize(manifest));
                    var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
                    using (var output = manifestEntry.Open()) output.Write(manifestBytes, 0, manifestBytes.Length);
                }

                string packageHash = ComputeSha256(outputPath);
                report("迁移包校验值：" + packageHash);
                return new ExportResult
                {
                    TotalThreads = active + archived,
                    FileCount = sources.Count,
                    PackageSha256 = packageHash
                };
            }
            catch
            {
                TryDeleteFile(outputPath);
                throw;
            }
            finally
            {
                TryDeleteDirectory(temp);
            }
        }

        public static ImportResult Import(string codexHome, string packagePath, Action<string> report)
        {
            PrepareCodexStopped(report);
            if (!File.Exists(packagePath)) throw new FileNotFoundException("迁移包不存在。", packagePath);
            if (!Directory.Exists(codexHome)) throw new InvalidOperationException("没有找到 Codex 数据目录。请先安装、登录并完整退出 Codex。");

            int existing = CountJsonl(Path.Combine(codexHome, "sessions")) + CountJsonl(Path.Combine(codexHome, "archived_sessions"));
            if (existing > 0)
            {
                throw new InvalidOperationException("目标电脑已有 " + existing + " 个聊天。为防止丢失数据，本工具拒绝直接覆盖。请在没有聊天的新 Codex 环境导入。");
            }

            string temp = CreateTempDirectory();
            string backup = null;
            bool targetModified = false;
            try
            {
                report("正在读取迁移包清单……");
                PackageManifest manifest;
                Dictionary<string, ZipArchiveEntry> entries;
                using (var archive = ZipFile.OpenRead(packagePath))
                {
                    var manifestEntry = archive.GetEntry("manifest.json");
                    if (manifestEntry == null) throw new InvalidDataException("不是有效的 Codex 聊天迁移包：缺少 manifest.json。");
                    using (var reader = new StreamReader(manifestEntry.Open(), Encoding.UTF8))
                        manifest = CreateSerializer().Deserialize<PackageManifest>(reader.ReadToEnd());

                    ValidateManifest(manifest);
                    entries = archive.Entries
                        .Where(e => !string.Equals(e.FullName, "manifest.json", StringComparison.OrdinalIgnoreCase))
                        .ToDictionary(e => NormalizeEntryPath(e.FullName), StringComparer.OrdinalIgnoreCase);

                    if (entries.Count != manifest.Files.Count) throw new InvalidDataException("迁移包文件数量与清单不一致。");
                    report("正在验证 " + manifest.Files.Count + " 个文件的完整性……");

                    foreach (var expected in manifest.Files)
                    {
                        ZipArchiveEntry entry;
                        string normalized = NormalizeEntryPath(expected.Path);
                        if (!entries.TryGetValue(normalized, out entry)) throw new InvalidDataException("迁移包缺少文件：" + expected.Path);
                        if (entry.Length != expected.Length) throw new InvalidDataException("文件长度校验失败：" + expected.Path);
                        string destination = GetSafeDestination(temp, normalized);
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        using (var input = entry.Open())
                        using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None)) input.CopyTo(output);
                        if (!SlowEquals(ComputeSha256(destination), expected.Sha256)) throw new InvalidDataException("SHA-256 校验失败：" + expected.Path);
                    }
                }

                PrepareCodexStopped(report);
                backup = BackupCurrentHistory(codexHome, report);
                report("正在写入聊天数据……");
                targetModified = true;

                foreach (string directory in HistoryDirectories)
                {
                    string source = Path.Combine(temp, directory);
                    if (Directory.Exists(source)) CopyDirectory(source, Path.Combine(codexHome, directory));
                }

                RemoveDatabaseFamily(codexHome, "state_5.sqlite");
                RemoveDatabaseFamily(codexHome, "goals_1.sqlite");
                foreach (string file in HistoryFiles)
                {
                    string source = Path.Combine(temp, file);
                    if (File.Exists(source)) File.Copy(source, Path.Combine(codexHome, file), true);
                }

                int imported = CountJsonl(Path.Combine(codexHome, "sessions")) + CountJsonl(Path.Combine(codexHome, "archived_sessions"));
                int expectedThreads = manifest.ActiveThreadFiles + manifest.ArchivedThreadFiles;
                if (imported != expectedThreads)
                    throw new IOException("导入后的聊天数量不匹配。已保留自动备份，请不要启动 Codex。预期 " + expectedThreads + "，实际 " + imported + "。");

                report("导入验证完成，共 " + imported + " 个聊天。");
                return new ImportResult { TotalThreads = imported, BackupPath = backup };
            }
            catch (Exception original)
            {
                if (targetModified && !string.IsNullOrEmpty(backup))
                {
                    try
                    {
                        report("导入未完成，正在自动恢复导入前状态……");
                        RestoreBackup(codexHome, backup);
                    }
                    catch (Exception restoreError)
                    {
                        throw new AggregateException("导入失败，自动恢复也未完成。备份仍保留在：" + backup, original, restoreError);
                    }
                }
                throw;
            }
            finally
            {
                TryDeleteDirectory(temp);
            }
        }

        public static int CountJsonl(string directory)
        {
            if (!Directory.Exists(directory)) return 0;
            return Directory.EnumerateFiles(directory, "*.jsonl", SearchOption.AllDirectories).Count();
        }

        private static void PrepareCodexStopped(Action<string> report)
        {
            if (TestMode) return;
            CodexProcessManager.StopCodex(report);
        }

        private static List<SourceFile> CollectRegularFiles(string codexHome)
        {
            var result = new List<SourceFile>();
            foreach (string directory in HistoryDirectories)
            {
                string full = Path.Combine(codexHome, directory);
                if (!Directory.Exists(full)) continue;
                AddDirectoryFiles(full, directory, result);
            }

            string index = Path.Combine(codexHome, "session_index.jsonl");
            if (File.Exists(index)) result.Add(new SourceFile(index, "session_index.jsonl"));
            return result;
        }

        private static void AddDirectoryFiles(string current, string relative, List<SourceFile> output)
        {
            var directoryInfo = new DirectoryInfo(current);
            if ((directoryInfo.Attributes & FileAttributes.ReparsePoint) != 0) return;
            foreach (string file in Directory.GetFiles(current))
            {
                var info = new FileInfo(file);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                output.Add(new SourceFile(file, NormalizeEntryPath(Path.Combine(relative, info.Name))));
            }
            foreach (string directory in Directory.GetDirectories(current))
                AddDirectoryFiles(directory, Path.Combine(relative, Path.GetFileName(directory)), output);
        }

        private static void AddDatabaseSnapshot(string codexHome, string temp, string name, bool sanitizeState, List<SourceFile> sources)
        {
            string source = Path.Combine(codexHome, name);
            if (!File.Exists(source)) return;
            string snapshot = Path.Combine(temp, name);
            File.Copy(source, snapshot, true);
            string wal = source + "-wal";
            if (File.Exists(wal)) File.Copy(wal, snapshot + "-wal", true);

            NativeSqlite.CheckpointAndSanitize(snapshot, sanitizeState);
            TryDeleteFile(snapshot + "-wal");
            TryDeleteFile(snapshot + "-shm");
            sources.Add(new SourceFile(snapshot, name));
        }

        private static void ValidateManifest(PackageManifest manifest)
        {
            if (manifest == null) throw new InvalidDataException("迁移包清单损坏。");
            if (manifest.FormatVersion != FormatVersion) throw new InvalidDataException("不支持的迁移包版本：" + manifest.FormatVersion);
            if (manifest.IncludesConfiguration) throw new InvalidDataException("该迁移包声称包含配置，本工具拒绝导入。");
            if (manifest.Files == null || manifest.Files.Count == 0) throw new InvalidDataException("迁移包不包含聊天文件。");

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in manifest.Files)
            {
                string path = NormalizeEntryPath(file.Path);
                if (!IsAllowedEntry(path)) throw new InvalidDataException("迁移包包含不允许的内容：" + path);
                if (!seen.Add(path)) throw new InvalidDataException("迁移包包含重复路径：" + path);
                if (file.Length < 0 || string.IsNullOrWhiteSpace(file.Sha256)) throw new InvalidDataException("迁移包清单字段无效：" + path);
            }
        }

        private static bool IsAllowedEntry(string path)
        {
            foreach (string directory in HistoryDirectories)
                if (path.StartsWith(directory + "/", StringComparison.OrdinalIgnoreCase)) return true;
            return HistoryFiles.Any(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
        }

        private static string BackupCurrentHistory(string codexHome, Action<string> report)
        {
            string root = Path.Combine(codexHome, "chat-migrator-backups");
            string backup = Path.Combine(root, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(backup);
            report("正在自动备份目标电脑现有聊天状态……");

            foreach (string directory in HistoryDirectories)
            {
                string source = Path.Combine(codexHome, directory);
                if (Directory.Exists(source)) CopyDirectory(source, Path.Combine(backup, directory));
            }
            foreach (string name in new[] { "session_index.jsonl", "state_5.sqlite", "state_5.sqlite-wal", "state_5.sqlite-shm", "goals_1.sqlite", "goals_1.sqlite-wal", "goals_1.sqlite-shm" })
            {
                string source = Path.Combine(codexHome, name);
                if (File.Exists(source)) File.Copy(source, Path.Combine(backup, name), true);
            }
            return backup;
        }

        private static void RestoreBackup(string codexHome, string backup)
        {
            foreach (string directory in HistoryDirectories)
            {
                string target = Path.Combine(codexHome, directory);
                if (Directory.Exists(target)) Directory.Delete(target, true);
                string source = Path.Combine(backup, directory);
                if (Directory.Exists(source)) CopyDirectory(source, target);
            }

            foreach (string name in new[] { "session_index.jsonl", "state_5.sqlite", "state_5.sqlite-wal", "state_5.sqlite-shm", "goals_1.sqlite", "goals_1.sqlite-wal", "goals_1.sqlite-shm" })
            {
                string target = Path.Combine(codexHome, name);
                TryDeleteFile(target);
                string source = Path.Combine(backup, name);
                if (File.Exists(source)) File.Copy(source, target, true);
            }
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
            foreach (string directory in Directory.GetDirectories(source)) CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }

        private static void RemoveDatabaseFamily(string directory, string name)
        {
            TryDeleteFile(Path.Combine(directory, name));
            TryDeleteFile(Path.Combine(directory, name + "-wal"));
            TryDeleteFile(Path.Combine(directory, name + "-shm"));
        }

        private static string ComputeSha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        private static bool SlowEquals(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int difference = 0;
            for (int i = 0; i < a.Length; i++) difference |= char.ToLowerInvariant(a[i]) ^ char.ToLowerInvariant(b[i]);
            return difference == 0;
        }

        private static string NormalizeEntryPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("迁移包包含空路径。");
            string normalized = path.Replace('\\', '/').TrimStart('/');
            if (normalized.Contains(":")) throw new InvalidDataException("迁移包包含非法路径：" + path);
            string[] parts = normalized.Split('/');
            if (parts.Any(p => p == ".." || p == "." || p.Length == 0)) throw new InvalidDataException("迁移包包含非法路径：" + path);
            return normalized;
        }

        private static string GetSafeDestination(string root, string relative)
        {
            string destination = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            string rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!destination.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("检测到路径穿越攻击。");
            return destination;
        }

        private static bool IsInside(string path, string directory)
        {
            string fullPath = Path.GetFullPath(path);
            string fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
        }

        private static string CreateTempDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "CodexPort-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static DateTimeOffset SafeZipTime(DateTime value)
        {
            if (value.Year < 1980) value = new DateTime(1980, 1, 1);
            if (value.Year > 2107) value = new DateTime(2107, 12, 31);
            return new DateTimeOffset(value);
        }

        private static JavaScriptSerializer CreateSerializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static void TryDeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
        }
    }

    internal static class CodexProcessManager
    {
        private const string CodexAppId = "OpenAI.Codex_2p2nqsd0c76g0!App";

        public static void StopCodex(Action<string> report)
        {
            var processes = FindCodexDesktopProcesses();
            if (processes.Count == 0)
            {
                report("Codex 当前未运行。");
                return;
            }

            report("检测到 Codex 正在运行，正在自动关闭……");
            foreach (var process in processes)
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero) process.CloseMainWindow();
                }
                catch { }
            }

            var gracefulDeadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < gracefulDeadline)
            {
                DisposeProcesses(processes);
                System.Threading.Thread.Sleep(250);
                processes = FindCodexDesktopProcesses();
                if (processes.Count == 0)
                {
                    report("Codex 已安全关闭。");
                    return;
                }
            }

            report("Codex 未在限时内退出，正在结束其剩余后台进程……");
            foreach (var process in processes.OrderByDescending(p => SafeProcessDepth(p)))
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
                catch { }
                finally { process.Dispose(); }
            }

            var forcedDeadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < forcedDeadline)
            {
                System.Threading.Thread.Sleep(250);
                processes = FindCodexDesktopProcesses();
                if (processes.Count == 0)
                {
                    report("Codex 已关闭。");
                    return;
                }
                DisposeProcesses(processes);
            }

            throw new InvalidOperationException("无法自动关闭 Codex。请保存正在进行的工作后重试。");
        }

        public static void StartCodex()
        {
            if (FindCodexDesktopProcessesAndDisposeIfAny()) return;
            var info = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "shell:AppsFolder\\" + CodexAppId,
                UseShellExecute = true
            };
            Process.Start(info);
        }

        private static bool FindCodexDesktopProcessesAndDisposeIfAny()
        {
            var processes = FindCodexDesktopProcesses();
            bool found = processes.Count > 0;
            DisposeProcesses(processes);
            return found;
        }

        private static List<Process> FindCodexDesktopProcesses()
        {
            var result = new List<Process>();
            foreach (string processName in new[] { "ChatGPT", "codex", "codex-code-mode-host" })
            {
                foreach (var process in Process.GetProcessesByName(processName))
                {
                    if (IsCodexDesktopProcess(process)) result.Add(process);
                    else process.Dispose();
                }
            }
            return result;
        }

        private static bool IsCodexDesktopProcess(Process process)
        {
            try
            {
                string path = process.MainModule.FileName;
                if (string.IsNullOrEmpty(path)) return false;
                string normalized = path.Replace('/', '\\');
                return normalized.IndexOf("\\WindowsApps\\OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       normalized.IndexOf("\\AppData\\Local\\OpenAI\\Codex\\", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private static int SafeProcessDepth(Process process)
        {
            string name = string.Empty;
            try { name = process.ProcessName; } catch { }
            if (string.Equals(name, "codex-code-mode-host", StringComparison.OrdinalIgnoreCase)) return 3;
            if (string.Equals(name, "codex", StringComparison.OrdinalIgnoreCase)) return 2;
            return 1;
        }

        private static void DisposeProcesses(IEnumerable<Process> processes)
        {
            foreach (var process in processes)
            {
                try { process.Dispose(); } catch { }
            }
        }
    }

    internal static class NativeSqlite
    {
        [DllImport("winsqlite3.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_open16(string filename, out IntPtr db);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_close(IntPtr db);

        [DllImport("winsqlite3.dll", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_exec(IntPtr db, string sql, IntPtr callback, IntPtr argument, out IntPtr errorMessage);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void sqlite3_free(IntPtr pointer);

        public static void CheckpointAndSanitize(string path, bool sanitizeState)
        {
            IntPtr db;
            int result = sqlite3_open16(path, out db);
            if (result != 0 || db == IntPtr.Zero) throw new IOException("无法打开聊天索引数据库快照，SQLite 错误 " + result + "。");
            try
            {
                Execute(db, "PRAGMA busy_timeout=10000;");
                if (sanitizeState)
                {
                    ExecuteAllowMissingTable(db, "DELETE FROM remote_control_enrollments;");
                    ExecuteAllowMissingTable(db, "DELETE FROM external_agent_config_imports;");
                }
                Execute(db, "PRAGMA wal_checkpoint(TRUNCATE);");
            }
            finally
            {
                sqlite3_close(db);
            }
        }

        private static void ExecuteAllowMissingTable(IntPtr db, string sql)
        {
            try { Execute(db, sql); }
            catch (InvalidOperationException ex)
            {
                if (ex.Message.IndexOf("no such table", StringComparison.OrdinalIgnoreCase) < 0) throw;
            }
        }

        private static void Execute(IntPtr db, string sql)
        {
            IntPtr error;
            int result = sqlite3_exec(db, sql, IntPtr.Zero, IntPtr.Zero, out error);
            if (result == 0) return;
            string message = error == IntPtr.Zero ? "未知 SQLite 错误" : Marshal.PtrToStringAnsi(error);
            if (error != IntPtr.Zero) sqlite3_free(error);
            throw new InvalidOperationException("SQLite 错误 " + result + "：" + message);
        }
    }

    internal sealed class SourceFile
    {
        public SourceFile(string sourcePath, string entryPath)
        {
            SourcePath = sourcePath;
            EntryPath = entryPath;
        }
        public string SourcePath { get; private set; }
        public string EntryPath { get; private set; }
    }

    public sealed class PackageManifest
    {
        public int FormatVersion { get; set; }
        public string ToolVersion { get; set; }
        public string CreatedUtc { get; set; }
        public int ActiveThreadFiles { get; set; }
        public int ArchivedThreadFiles { get; set; }
        public bool IncludesConfiguration { get; set; }
        public List<ManifestFile> Files { get; set; }
    }

    public sealed class ManifestFile
    {
        public string Path { get; set; }
        public long Length { get; set; }
        public string Sha256 { get; set; }
    }

    internal sealed class ExportResult
    {
        public int TotalThreads { get; set; }
        public int FileCount { get; set; }
        public string PackageSha256 { get; set; }
    }

    internal sealed class ImportResult
    {
        public int TotalThreads { get; set; }
        public string BackupPath { get; set; }
    }
}
