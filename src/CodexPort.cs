using System;
using System.Collections;
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
[assembly: AssemblyDescription("Merge local Codex chats across Windows computers without copying configuration or credentials.")]
[assembly: AssemblyCompany("Local utility")]
[assembly: AssemblyProduct("CodexPort")]
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]

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
                Text = "增量合并多台电脑的 Codex 聊天；保留本机记录，不迁移登录、配置、插件、技能或密钥。",
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

            exportButton = CreatePrimaryButton("导出聊天包", new Point(131, 139));
            importButton = CreateSecondaryButton("导入聊天包", new Point(361, 139));
            exportButton.Click += ExportButtonClick;
            importButton.Click += ImportButtonClick;

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
                title, subtitle, locationLabel, exportButton, importButton,
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
                    return "合并完成\r\n本机原有：" + result.ExistingThreads + " 个\r\n新增聊天：" + result.AddedThreads +
                           " 个\r\n重复跳过：" + result.SkippedDuplicates + " 个\r\n冲突副本：" + result.ConflictCopies +
                           " 个\r\n合并后总数：" + result.TotalThreads + " 个\r\n新增资源：" + result.AddedResources +
                           " 个\r\n" + launchStatus + "\r\n\r\n自动备份：\r\n" + result.BackupPath;
                });
            }
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
        private const int FormatVersion = 2;
        private const string ToolVersion = "1.2.0";
        private const int MaximumPackageFiles = 100000;
        private const long MaximumSingleFileBytes = 4L * 1024L * 1024L * 1024L;
        private const long MaximumExpandedBytes = 50L * 1024L * 1024L * 1024L;
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
                    SourceCodexHome = codexHome,
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
            string temp = CreateTempDirectory();
            string staging = CreateTempDirectory();
            string backup = null;
            bool targetModified = false;
            try
            {
                report("正在读取迁移包清单……");
                PackageManifest manifest;
                Dictionary<string, ZipArchiveEntry> entries;
                using (var archive = ZipFile.OpenRead(packagePath))
                {
                    var manifestEntries = archive.Entries.Where(e => string.Equals(NormalizeEntryPath(e.FullName), "manifest.json", StringComparison.OrdinalIgnoreCase)).ToList();
                    if (manifestEntries.Count != 1) throw new InvalidDataException("不是有效的 Codex 聊天迁移包：manifest.json 数量必须为 1。");
                    var manifestEntry = manifestEntries[0];
                    if (manifestEntry.Length > 8L * 1024L * 1024L) throw new InvalidDataException("迁移包清单过大。");
                    using (var reader = new StreamReader(manifestEntry.Open(), Encoding.UTF8))
                        manifest = CreateSerializer().Deserialize<PackageManifest>(reader.ReadToEnd());

                    ValidateManifest(manifest);
                    entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
                    foreach (var entry in archive.Entries)
                    {
                        string normalized = NormalizeEntryPath(entry.FullName);
                        if (string.Equals(normalized, "manifest.json", StringComparison.OrdinalIgnoreCase)) continue;
                        if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) throw new InvalidDataException("迁移包包含未声明的目录项：" + entry.FullName);
                        if (entries.ContainsKey(normalized)) throw new InvalidDataException("迁移包包含重复路径：" + normalized);
                        entries.Add(normalized, entry);
                    }

                    if (entries.Count != manifest.Files.Count) throw new InvalidDataException("迁移包文件数量与清单不一致。");
                    report("正在验证 " + manifest.Files.Count + " 个文件的完整性……");

                    long expandedBytes = 0;

                    foreach (var expected in manifest.Files)
                    {
                        ZipArchiveEntry entry;
                        string normalized = NormalizeEntryPath(expected.Path);
                        if (!entries.TryGetValue(normalized, out entry)) throw new InvalidDataException("迁移包缺少文件：" + expected.Path);
                        if (entry.Length != expected.Length) throw new InvalidDataException("文件长度校验失败：" + expected.Path);
                        expandedBytes = checked(expandedBytes + expected.Length);
                        if (expected.Length > MaximumSingleFileBytes || expandedBytes > MaximumExpandedBytes)
                            throw new InvalidDataException("迁移包展开后超过安全大小限制。");
                        if (entry.CompressedLength > 0 && entry.Length / Math.Max(1L, entry.CompressedLength) > 10000L)
                            throw new InvalidDataException("迁移包中存在异常压缩比文件：" + expected.Path);
                        string destination = GetSafeDestination(temp, normalized);
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        using (var input = entry.Open())
                        using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None)) input.CopyTo(output);
                        if (!SlowEquals(ComputeSha256(destination), expected.Sha256)) throw new InvalidDataException("SHA-256 校验失败：" + expected.Path);
                    }
                }

                PrepareCodexStopped(report);
                string sourceCodexHome = ResolveSourceCodexHome(manifest, temp);
                var targetThreads = ScanThreadFiles(codexHome, codexHome);
                var sourceThreads = ScanThreadFiles(temp, sourceCodexHome);
                if (sourceThreads.Count != manifest.ActiveThreadFiles + manifest.ArchivedThreadFiles)
                    throw new InvalidDataException("迁移包中的聊天数量与清单不一致。");

                report("正在计算增量合并方案……");
                MergePlan plan = BuildMergePlan(codexHome, sourceCodexHome, sourceThreads, targetThreads);
                List<ResourceImportPlan> resources = BuildResourcePlan(temp, codexHome, plan.IdMap);
                plan.AddedResources = resources.Count(r => r.ShouldCopy);

                if (plan.AddedThreads == 0 && plan.AddedResources == 0)
                {
                    report("迁移包中的聊天和资源均已存在，无需修改本机数据。");
                    return new ImportResult
                    {
                        ExistingThreads = targetThreads.Count,
                        AddedThreads = 0,
                        SkippedDuplicates = plan.SkippedDuplicates,
                        ConflictCopies = 0,
                        TotalThreads = targetThreads.Count,
                        AddedResources = 0,
                        BackupPath = "未创建（没有需要合并的内容）"
                    };
                }

                report("正在生成合并后的聊天与索引……");
                foreach (var thread in plan.Threads.Where(t => t.ShouldAdd))
                {
                    string stagedThread = GetSafeDestination(staging, thread.DestinationRelativePath);
                    RewriteThreadFile(thread.SourcePath, stagedThread, plan.IdMap, sourceCodexHome, codexHome);
                }
                string stagedIndex = BuildMergedSessionIndex(codexHome, temp, staging, plan);

                string stagedState = null;
                string stagedGoals = null;
                if (plan.AddedThreads > 0)
                {
                    string sourceState = Path.Combine(temp, "state_5.sqlite");
                    string targetState = Path.Combine(codexHome, "state_5.sqlite");
                    if (!File.Exists(sourceState) || !File.Exists(targetState))
                        throw new InvalidDataException("两台电脑都需要由当前 Codex 初始化聊天索引后才能安全合并。");

                    var sourceDatabaseIds = new HashSet<string>(NativeSqlite.GetThreadIds(sourceState), StringComparer.OrdinalIgnoreCase);
                    foreach (var thread in plan.Threads.Where(t => t.ShouldAdd))
                        if (!sourceDatabaseIds.Contains(thread.OriginalId)) throw new InvalidDataException("来源索引缺少聊天：" + thread.OriginalId);

                    stagedState = Path.Combine(staging, "state_5.sqlite");
                    CreateDatabaseSnapshot(targetState, stagedState);
                    NativeSqlite.MergeStateDatabases(stagedState, sourceState, plan.Threads);

                    string sourceGoals = Path.Combine(temp, "goals_1.sqlite");
                    string targetGoals = Path.Combine(codexHome, "goals_1.sqlite");
                    if (File.Exists(sourceGoals))
                    {
                        if (!File.Exists(targetGoals)) throw new InvalidDataException("目标 Codex 缺少目标状态数据库，请先升级并启动一次 Codex。");
                        stagedGoals = Path.Combine(staging, "goals_1.sqlite");
                        CreateDatabaseSnapshot(targetGoals, stagedGoals);
                        NativeSqlite.MergeGoalDatabases(stagedGoals, sourceGoals, plan.Threads);
                    }
                }

                backup = BackupCurrentHistory(codexHome, report);
                report("正在写入新增聊天，保留本机已有内容……");
                targetModified = true;

                foreach (var thread in plan.Threads.Where(t => t.ShouldAdd))
                {
                    string stagedThread = GetSafeDestination(staging, thread.DestinationRelativePath);
                    string targetThread = GetSafeDestination(codexHome, thread.DestinationRelativePath);
                    CopyFileCreateNew(stagedThread, targetThread);
                }
                foreach (var resource in resources.Where(r => r.ShouldCopy))
                    CopyFileCreateNew(resource.SourcePath, GetSafeDestination(codexHome, resource.DestinationRelativePath));

                if (!string.IsNullOrEmpty(stagedIndex)) ReplaceFileFromStage(stagedIndex, Path.Combine(codexHome, "session_index.jsonl"));
                if (!string.IsNullOrEmpty(stagedState)) ReplaceDatabaseFromStage(stagedState, codexHome, "state_5.sqlite");
                if (!string.IsNullOrEmpty(stagedGoals)) ReplaceDatabaseFromStage(stagedGoals, codexHome, "goals_1.sqlite");

                var mergedThreads = ScanThreadFiles(codexHome, codexHome);
                int expectedTotal = targetThreads.Count + plan.AddedThreads;
                if (mergedThreads.Count != expectedTotal)
                    throw new IOException("合并后的聊天数量不匹配。预期 " + expectedTotal + "，实际 " + mergedThreads.Count + "。");
                var mergedDatabaseIds = new HashSet<string>(NativeSqlite.GetThreadIds(Path.Combine(codexHome, "state_5.sqlite")), StringComparer.OrdinalIgnoreCase);
                foreach (var thread in plan.Threads.Where(t => t.ShouldAdd))
                    if (!mergedDatabaseIds.Contains(thread.EffectiveId)) throw new IOException("合并后的索引缺少聊天：" + thread.EffectiveId);
                NativeSqlite.ValidateDatabase(Path.Combine(codexHome, "state_5.sqlite"));
                if (File.Exists(Path.Combine(codexHome, "goals_1.sqlite"))) NativeSqlite.ValidateDatabase(Path.Combine(codexHome, "goals_1.sqlite"));

                report("增量合并验证完成，共 " + mergedThreads.Count + " 个聊天。");
                return new ImportResult
                {
                    ExistingThreads = targetThreads.Count,
                    AddedThreads = plan.AddedThreads,
                    SkippedDuplicates = plan.SkippedDuplicates,
                    ConflictCopies = plan.ConflictCopies,
                    TotalThreads = mergedThreads.Count,
                    AddedResources = plan.AddedResources,
                    BackupPath = backup
                };
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
                TryDeleteDirectory(staging);
            }
        }

        public static int CountJsonl(string directory)
        {
            if (!Directory.Exists(directory)) return 0;
            return Directory.EnumerateFiles(directory, "*.jsonl", SearchOption.AllDirectories).Count();
        }

        private static Dictionary<string, ThreadFileInfo> ScanThreadFiles(string root, string pathRootForFingerprint)
        {
            var result = new Dictionary<string, ThreadFileInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (string directory in new[] { "sessions", "archived_sessions" })
            {
                string full = Path.Combine(root, directory);
                if (!Directory.Exists(full)) continue;
                foreach (string file in Directory.EnumerateFiles(full, "*.jsonl", SearchOption.AllDirectories))
                {
                    string id = ReadCanonicalThreadId(file);
                    if (result.ContainsKey(id)) throw new InvalidDataException("检测到重复聊天 ID：" + id);
                    string relative = NormalizeEntryPath(file.Substring(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar).Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    result.Add(id, new ThreadFileInfo
                    {
                        Id = id,
                        SourcePath = file,
                        RelativePath = relative,
                        Archived = relative.StartsWith("archived_sessions/", StringComparison.OrdinalIgnoreCase),
                        Fingerprint = ComputeThreadFingerprint(file, pathRootForFingerprint, id)
                    });
                }
            }
            return result;
        }

        private static string ReadCanonicalThreadId(string path)
        {
            using (var reader = new StreamReader(path, Encoding.UTF8, true))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    Dictionary<string, object> root = CreateSerializer().DeserializeObject(line) as Dictionary<string, object>;
                    if (root == null || !string.Equals(GetString(root, "type"), "session_meta", StringComparison.Ordinal))
                        throw new InvalidDataException("聊天文件首条记录不是 session_meta：" + path);
                    Dictionary<string, object> payload = GetDictionary(root, "payload");
                    string id = payload == null ? null : GetString(payload, "id");
                    Guid parsed;
                    if (string.IsNullOrWhiteSpace(id) || !Guid.TryParse(id, out parsed))
                        throw new InvalidDataException("聊天文件缺少有效 ID：" + path);
                    return parsed.ToString("D");
                }
            }
            throw new InvalidDataException("聊天文件为空：" + path);
        }

        private static MergePlan BuildMergePlan(
            string codexHome,
            string sourceCodexHome,
            Dictionary<string, ThreadFileInfo> sourceThreads,
            Dictionary<string, ThreadFileInfo> targetThreads)
        {
            var plan = new MergePlan { ExistingThreads = targetThreads.Count, SourceCodexHome = sourceCodexHome };
            var fingerprints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var ambiguousFingerprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var target in targetThreads.Values)
            {
                string prior;
                if (fingerprints.TryGetValue(target.Fingerprint, out prior))
                {
                    fingerprints.Remove(target.Fingerprint);
                    ambiguousFingerprints.Add(target.Fingerprint);
                }
                else if (!ambiguousFingerprints.Contains(target.Fingerprint)) fingerprints.Add(target.Fingerprint, target.Id);
            }

            var claimedIds = new HashSet<string>(targetThreads.Keys, StringComparer.OrdinalIgnoreCase);
            var usedPaths = new HashSet<string>(targetThreads.Values.Select(t => t.RelativePath), StringComparer.OrdinalIgnoreCase);

            foreach (var source in sourceThreads.Values.OrderBy(t => t.Id, StringComparer.OrdinalIgnoreCase))
            {
                string effectiveId = source.Id;
                bool shouldAdd = false;
                bool conflict = false;
                ThreadFileInfo sameIdTarget;
                string contentMatchId;

                if (targetThreads.TryGetValue(source.Id, out sameIdTarget) && SlowEquals(sameIdTarget.Fingerprint, source.Fingerprint))
                {
                    plan.SkippedDuplicates++;
                }
                else if (fingerprints.TryGetValue(source.Fingerprint, out contentMatchId))
                {
                    effectiveId = contentMatchId;
                    plan.SkippedDuplicates++;
                }
                else if (sameIdTarget != null)
                {
                    effectiveId = CreateDeterministicConflictId(source.Id, source.Fingerprint);
                    ThreadFileInfo existingConflict;
                    if (targetThreads.TryGetValue(effectiveId, out existingConflict))
                    {
                        if (!SlowEquals(existingConflict.Fingerprint, source.Fingerprint))
                            throw new InvalidDataException("确定性冲突副本 ID 已被其他聊天占用：" + effectiveId);
                        plan.SkippedDuplicates++;
                    }
                    else if (claimedIds.Contains(effectiveId))
                    {
                        throw new InvalidDataException("合并计划出现聊天 ID 冲突：" + effectiveId);
                    }
                    else
                    {
                        shouldAdd = true;
                        conflict = true;
                    }
                }
                else
                {
                    if (claimedIds.Contains(effectiveId)) throw new InvalidDataException("合并计划出现聊天 ID 冲突：" + effectiveId);
                    shouldAdd = true;
                }

                string destinationRelative = null;
                if (shouldAdd)
                {
                    destinationRelative = RemapThreadRelativePath(source.RelativePath, source.Id, effectiveId);
                    if (usedPaths.Contains(destinationRelative) || File.Exists(GetSafeDestination(codexHome, destinationRelative)))
                    {
                        string directory = NormalizeEntryPath(Path.GetDirectoryName(destinationRelative) ?? (source.Archived ? "archived_sessions" : "sessions"));
                        destinationRelative = directory + "/rollout-imported-" + effectiveId + ".jsonl";
                    }
                    if (!usedPaths.Add(destinationRelative)) throw new InvalidDataException("合并计划出现目标路径冲突：" + destinationRelative);
                    claimedIds.Add(effectiveId);
                    fingerprints[source.Fingerprint] = effectiveId;
                    plan.AddedThreads++;
                    if (conflict) plan.ConflictCopies++;
                }

                plan.IdMap[source.Id] = effectiveId;
                plan.Threads.Add(new ThreadImportPlan
                {
                    OriginalId = source.Id,
                    EffectiveId = effectiveId,
                    SourcePath = source.SourcePath,
                    SourceRelativePath = source.RelativePath,
                    DestinationRelativePath = destinationRelative,
                    DestinationPath = shouldAdd ? GetSafeDestination(codexHome, destinationRelative) : null,
                    Fingerprint = source.Fingerprint,
                    Archived = source.Archived,
                    ShouldAdd = shouldAdd,
                    IsConflict = conflict
                });
            }
            return plan;
        }

        private static string RemapThreadRelativePath(string relative, string oldId, string newId)
        {
            if (string.Equals(oldId, newId, StringComparison.OrdinalIgnoreCase)) return relative;
            return ReplaceOrdinalIgnoreCase(relative, oldId, newId);
        }

        private static string CreateDeterministicConflictId(string originalId, string fingerprint)
        {
            byte[] input = Encoding.UTF8.GetBytes("CodexPort-v1.2-conflict:" + originalId.ToLowerInvariant() + ":" + fingerprint.ToLowerInvariant());
            byte[] hash;
            using (var sha = SHA256.Create()) hash = sha.ComputeHash(input);
            byte[] guid = new byte[16];
            Buffer.BlockCopy(hash, 0, guid, 0, 16);
            guid[7] = (byte)((guid[7] & 0x0F) | 0x50);
            guid[8] = (byte)((guid[8] & 0x3F) | 0x80);
            return new Guid(guid).ToString("D");
        }

        private static List<ResourceImportPlan> BuildResourcePlan(string sourceRoot, string codexHome, Dictionary<string, string> idMap)
        {
            var result = new List<ResourceImportPlan>();
            foreach (string directory in new[] { "codex-remote-attachments", "generated_images", "visualizations" })
            {
                string full = Path.Combine(sourceRoot, directory);
                if (!Directory.Exists(full)) continue;
                foreach (string source in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
                {
                    string relative = NormalizeEntryPath(source.Substring(Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar).Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    string destinationRelative = RemapResourcePath(relative, idMap);
                    string destination = GetSafeDestination(codexHome, destinationRelative);
                    bool copy = !File.Exists(destination);
                    string hash = ComputeSha256(source);
                    if (!copy && !SlowEquals(hash, ComputeSha256(destination)))
                        throw new IOException("两台电脑存在同路径但内容不同的聊天资源，为避免断链已停止合并：" + destinationRelative);
                    result.Add(new ResourceImportPlan
                    {
                        SourcePath = source,
                        DestinationRelativePath = destinationRelative,
                        Sha256 = hash,
                        ShouldCopy = copy
                    });
                }
            }
            return result;
        }

        private static string RemapResourcePath(string relative, Dictionary<string, string> idMap)
        {
            string[] parts = relative.Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                string mapped;
                if (idMap.TryGetValue(parts[i], out mapped)) parts[i] = mapped;
                else
                {
                    foreach (var pair in idMap)
                        if (!string.Equals(pair.Key, pair.Value, StringComparison.OrdinalIgnoreCase) && parts[i].IndexOf(pair.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                            parts[i] = ReplaceOrdinalIgnoreCase(parts[i], pair.Key, pair.Value);
                }
            }
            return string.Join("/", parts);
        }

        private static string ComputeThreadFingerprint(string path, string codexHome, string canonicalId)
        {
            var identityMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { canonicalId, "$THREAD_ID" } };
            using (var sha = SHA256.Create())
            using (var reader = new StreamReader(path, Encoding.UTF8, true))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    object value = CreateSerializer().DeserializeObject(line);
                    object normalized = TransformJsonValue(value, identityMap, codexHome, "$CODEX_HOME", 0);
                    byte[] bytes = Encoding.UTF8.GetBytes(CreateSerializer().Serialize(normalized) + "\n");
                    sha.TransformBlock(bytes, 0, bytes.Length, bytes, 0);
                }
                sha.TransformFinalBlock(new byte[0], 0, 0);
                return BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private static void RewriteThreadFile(
            string sourcePath,
            string destinationPath,
            Dictionary<string, string> idMap,
            string sourceCodexHome,
            string targetCodexHome)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
            using (var reader = new StreamReader(sourcePath, Encoding.UTF8, true))
            using (var writer = new StreamWriter(destinationPath, false, new UTF8Encoding(false)))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    object value = CreateSerializer().DeserializeObject(line);
                    writer.WriteLine(CreateSerializer().Serialize(TransformJsonValue(value, idMap, sourceCodexHome, targetCodexHome, 0)));
                }
            }
        }

        private static object TransformJsonValue(
            object value,
            Dictionary<string, string> idMap,
            string sourceCodexHome,
            string targetCodexHome,
            int depth)
        {
            if (value == null || depth > 120) return value;
            var dictionary = value as Dictionary<string, object>;
            if (dictionary != null)
            {
                foreach (string key in dictionary.Keys.ToList())
                    dictionary[key] = TransformJsonValue(dictionary[key], idMap, sourceCodexHome, targetCodexHome, depth + 1);
                return dictionary;
            }
            var array = value as object[];
            if (array != null)
            {
                for (int i = 0; i < array.Length; i++) array[i] = TransformJsonValue(array[i], idMap, sourceCodexHome, targetCodexHome, depth + 1);
                return array;
            }
            var list = value as ArrayList;
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++) list[i] = TransformJsonValue(list[i], idMap, sourceCodexHome, targetCodexHome, depth + 1);
                return list;
            }
            string text = value as string;
            if (text == null) return value;

            string mapped;
            if (idMap.TryGetValue(text, out mapped)) return mapped;
            string transformed = ReplaceKnownAssetPaths(text, idMap, sourceCodexHome, targetCodexHome);

            string trimmed = transformed.Trim();
            if (depth < 30 && trimmed.Length > 1 && ((trimmed[0] == '{' && trimmed[trimmed.Length - 1] == '}') || (trimmed[0] == '[' && trimmed[trimmed.Length - 1] == ']')))
            {
                try
                {
                    object nested = CreateSerializer().DeserializeObject(transformed);
                    return CreateSerializer().Serialize(TransformJsonValue(nested, idMap, sourceCodexHome, targetCodexHome, depth + 1));
                }
                catch { }
            }
            return transformed;
        }

        private static string ReplaceKnownAssetPaths(string text, Dictionary<string, string> idMap, string sourceCodexHome, string targetCodexHome)
        {
            string result = text;
            bool containsAssets = false;
            foreach (string directory in new[] { "codex-remote-attachments", "generated_images", "visualizations" })
            {
                if (result.IndexOf(directory, StringComparison.OrdinalIgnoreCase) < 0) continue;
                containsAssets = true;
                if (!string.IsNullOrWhiteSpace(sourceCodexHome))
                {
                    string sourceBackslash = sourceCodexHome.TrimEnd('\\', '/') + "\\" + directory;
                    string targetBackslash = targetCodexHome.TrimEnd('\\', '/') + "\\" + directory;
                    result = ReplaceOrdinalIgnoreCase(result, sourceBackslash, targetBackslash);
                    result = ReplaceOrdinalIgnoreCase(result, sourceBackslash.Replace("\\", "\\\\"), targetBackslash.Replace("\\", "\\\\"));
                    string sourceSlash = sourceCodexHome.Replace('\\', '/').TrimEnd('/') + "/" + directory;
                    string targetSlash = targetCodexHome.Replace('\\', '/').TrimEnd('/') + "/" + directory;
                    result = ReplaceOrdinalIgnoreCase(result, sourceSlash, targetSlash);
                }
            }
            if (containsAssets)
            {
                foreach (var pair in idMap)
                    if (!string.Equals(pair.Key, pair.Value, StringComparison.OrdinalIgnoreCase))
                        result = ReplaceOrdinalIgnoreCase(result, pair.Key, pair.Value);
            }
            return result;
        }

        private static string ReplaceOrdinalIgnoreCase(string value, string oldValue, string newValue)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(oldValue)) return value;
            var builder = new StringBuilder();
            int start = 0;
            int index;
            while ((index = value.IndexOf(oldValue, start, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                builder.Append(value, start, index - start);
                builder.Append(newValue);
                start = index + oldValue.Length;
            }
            if (start == 0) return value;
            builder.Append(value, start, value.Length - start);
            return builder.ToString();
        }

        private static string ResolveSourceCodexHome(PackageManifest manifest, string extractedRoot)
        {
            if (!string.IsNullOrWhiteSpace(manifest.SourceCodexHome)) return manifest.SourceCodexHome.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string state = Path.Combine(extractedRoot, "state_5.sqlite");
            if (File.Exists(state))
            {
                string rolloutPath = NativeSqlite.GetFirstThreadPath(state);
                if (!string.IsNullOrWhiteSpace(rolloutPath))
                {
                    foreach (string marker in new[] { "\\sessions\\", "\\archived_sessions\\", "/sessions/", "/archived_sessions/" })
                    {
                        int index = rolloutPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                        if (index > 0) return rolloutPath.Substring(0, index);
                    }
                }
            }
            return string.Empty;
        }

        private static string BuildMergedSessionIndex(string codexHome, string sourceRoot, string staging, MergePlan plan)
        {
            string sourcePath = Path.Combine(sourceRoot, "session_index.jsonl");
            if (!File.Exists(sourcePath) || plan.AddedThreads == 0) return null;
            string targetPath = Path.Combine(codexHome, "session_index.jsonl");
            var ordered = new List<Dictionary<string, object>>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(targetPath)) ReadIndexLines(targetPath, ordered, ids, null, null);

            var plansByOriginal = plan.Threads.ToDictionary(t => t.OriginalId, StringComparer.OrdinalIgnoreCase);
            var sourceEntries = new List<Dictionary<string, object>>();
            var ignoredSourceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ReadIndexLines(sourcePath, sourceEntries, ignoredSourceIds, null, null);
            foreach (var entry in sourceEntries)
            {
                string originalId = GetString(entry, "id");
                ThreadImportPlan thread;
                if (string.IsNullOrWhiteSpace(originalId) || !plansByOriginal.TryGetValue(originalId, out thread) || !thread.ShouldAdd) continue;
                var transformed = TransformJsonValue(entry, plan.IdMap, plan.SourceCodexHome, codexHome, 0) as Dictionary<string, object>;
                if (transformed == null || !ids.Add(thread.EffectiveId)) continue;
                transformed["id"] = thread.EffectiveId;
                if (thread.IsConflict)
                {
                    string name = GetString(transformed, "thread_name");
                    if (!string.IsNullOrWhiteSpace(name)) transformed["thread_name"] = name + "（导入副本）";
                }
                ordered.Add(transformed);
            }

            string output = Path.Combine(staging, "session_index.jsonl");
            using (var writer = new StreamWriter(output, false, new UTF8Encoding(false)))
                foreach (var entry in ordered) writer.WriteLine(CreateSerializer().Serialize(entry));
            return output;
        }

        private static void ReadIndexLines(
            string path,
            List<Dictionary<string, object>> output,
            HashSet<string> ids,
            Dictionary<string, string> idMap,
            string targetRoot)
        {
            foreach (string line in File.ReadLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var entry = CreateSerializer().DeserializeObject(line) as Dictionary<string, object>;
                if (entry == null) throw new InvalidDataException("聊天标题索引损坏：" + path);
                string id = GetString(entry, "id");
                if (string.IsNullOrWhiteSpace(id)) throw new InvalidDataException("聊天标题索引缺少 ID：" + path);
                if (!ids.Add(id)) continue;
                output.Add(entry);
            }
        }

        private static string GetString(Dictionary<string, object> dictionary, string key)
        {
            object value;
            return dictionary != null && dictionary.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : null;
        }

        private static Dictionary<string, object> GetDictionary(Dictionary<string, object> dictionary, string key)
        {
            object value;
            return dictionary != null && dictionary.TryGetValue(key, out value) ? value as Dictionary<string, object> : null;
        }

        private static void CreateDatabaseSnapshot(string source, string destination)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.Copy(source, destination, true);
            if (File.Exists(source + "-wal")) File.Copy(source + "-wal", destination + "-wal", true);
            NativeSqlite.CheckpointAndSanitize(destination, false);
            TryDeleteFile(destination + "-wal");
            TryDeleteFile(destination + "-shm");
        }

        private static void CopyFileCreateNew(string source, string destination)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None)) input.CopyTo(output);
        }

        private static void ReplaceFileFromStage(string source, string destination)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            string pending = destination + ".codexport-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.Copy(source, pending, false);
                if (File.Exists(destination)) File.Replace(pending, destination, null, true);
                else File.Move(pending, destination);
            }
            finally { TryDeleteFile(pending); }
        }

        private static void ReplaceDatabaseFromStage(string source, string targetDirectory, string name)
        {
            TryDeleteFile(Path.Combine(targetDirectory, name + "-wal"));
            TryDeleteFile(Path.Combine(targetDirectory, name + "-shm"));
            ReplaceFileFromStage(source, Path.Combine(targetDirectory, name));
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
            if (manifest.FormatVersion < 1 || manifest.FormatVersion > FormatVersion) throw new InvalidDataException("不支持的迁移包版本：" + manifest.FormatVersion);
            if (manifest.IncludesConfiguration) throw new InvalidDataException("该迁移包声称包含配置，本工具拒绝导入。");
            if (manifest.Files == null || manifest.Files.Count == 0) throw new InvalidDataException("迁移包不包含聊天文件。");
            if (manifest.Files.Count > MaximumPackageFiles) throw new InvalidDataException("迁移包文件数量超过安全限制。");
            if (manifest.FormatVersion >= 2 && string.IsNullOrWhiteSpace(manifest.SourceCodexHome)) throw new InvalidDataException("迁移包缺少来源数据目录信息。");

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var file in manifest.Files)
            {
                string path = NormalizeEntryPath(file.Path);
                if (!IsAllowedEntry(path)) throw new InvalidDataException("迁移包包含不允许的内容：" + path);
                if (!seen.Add(path)) throw new InvalidDataException("迁移包包含重复路径：" + path);
                if (file.Length < 0 || file.Length > MaximumSingleFileBytes || !IsSha256(file.Sha256)) throw new InvalidDataException("迁移包清单字段无效：" + path);
                total = checked(total + file.Length);
                if (total > MaximumExpandedBytes) throw new InvalidDataException("迁移包展开后超过安全大小限制。");
            }
        }

        private static bool IsAllowedEntry(string path)
        {
            if (path.StartsWith("sessions/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("archived_sessions/", StringComparison.OrdinalIgnoreCase))
                return path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase);
            foreach (string directory in new[] { "codex-remote-attachments", "generated_images", "visualizations" })
                if (path.StartsWith(directory + "/", StringComparison.OrdinalIgnoreCase)) return true;
            return HistoryFiles.Any(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsSha256(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 64) return false;
            foreach (char character in value)
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f') || (character >= 'A' && character <= 'F'))) return false;
            return true;
        }

        private static string BackupCurrentHistory(string codexHome, Action<string> report)
        {
            string root = Path.Combine(codexHome, "chat-migrator-backups");
            string backup = Path.Combine(root, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
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
                if (File.Exists(target)) File.Delete(target);
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

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int SqliteCallback(IntPtr argument, int columnCount, IntPtr values, IntPtr names);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_exec", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_exec_utf8(IntPtr db, byte[] sql, SqliteCallback callback, IntPtr argument, out IntPtr errorMessage);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void sqlite3_free(IntPtr pointer);

        public static void CheckpointAndSanitize(string path, bool sanitizeState)
        {
            IntPtr db = Open(path);
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

        public static List<string> GetThreadIds(string path)
        {
            IntPtr db = Open(path);
            try
            {
                if (!TableExists(db, "main", "threads")) throw new InvalidDataException("聊天索引数据库缺少 threads 表。");
                return Query(db, "SELECT id FROM threads;").Select(r => r.ContainsKey("id") ? r["id"] : null).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
            }
            finally { sqlite3_close(db); }
        }

        public static string GetFirstThreadPath(string path)
        {
            IntPtr db = Open(path);
            try
            {
                if (!TableExists(db, "main", "threads")) return null;
                var rows = Query(db, "SELECT rollout_path FROM threads LIMIT 1;");
                return rows.Count == 0 || !rows[0].ContainsKey("rollout_path") ? null : rows[0]["rollout_path"];
            }
            finally { sqlite3_close(db); }
        }

        public static void MergeStateDatabases(string targetPath, string sourcePath, List<ThreadImportPlan> plans)
        {
            IntPtr db = Open(targetPath);
            bool attached = false;
            bool transaction = false;
            try
            {
                Execute(db, "PRAGMA busy_timeout=10000; PRAGMA foreign_keys=OFF;");
                Execute(db, "ATTACH DATABASE " + SqlLiteral(sourcePath) + " AS codexport_source;");
                attached = true;
                RequireCompatibleTable(db, "threads", true);
                Execute(db, "BEGIN IMMEDIATE;");
                transaction = true;
                CreateMappingTable(db, plans);

                if (TableExists(db, "main", "thread_sections") && TableExists(db, "codexport_source", "thread_sections"))
                    InsertSections(db);
                InsertThreads(db);
                if (TableExists(db, "main", "thread_dynamic_tools") && TableExists(db, "codexport_source", "thread_dynamic_tools"))
                    InsertMappedRows(db, "thread_dynamic_tools", "thread_id", "thread_id");
                if (TableExists(db, "main", "thread_spawn_edges") && TableExists(db, "codexport_source", "thread_spawn_edges"))
                    InsertSpawnEdges(db);

                Execute(db, "COMMIT;");
                transaction = false;
                Execute(db, "DETACH DATABASE codexport_source;");
                attached = false;
                ValidateOpenDatabase(db);
                Execute(db, "PRAGMA wal_checkpoint(TRUNCATE);");
            }
            catch
            {
                if (transaction) { try { Execute(db, "ROLLBACK;"); } catch { } }
                if (attached) { try { Execute(db, "DETACH DATABASE codexport_source;"); } catch { } }
                throw;
            }
            finally { sqlite3_close(db); }
        }

        public static void MergeGoalDatabases(string targetPath, string sourcePath, List<ThreadImportPlan> plans)
        {
            IntPtr db = Open(targetPath);
            bool attached = false;
            bool transaction = false;
            try
            {
                Execute(db, "PRAGMA busy_timeout=10000; PRAGMA foreign_keys=OFF;");
                Execute(db, "ATTACH DATABASE " + SqlLiteral(sourcePath) + " AS codexport_source;");
                attached = true;
                Execute(db, "BEGIN IMMEDIATE;");
                transaction = true;
                CreateMappingTable(db, plans);
                if (TableExists(db, "main", "thread_goals") && TableExists(db, "codexport_source", "thread_goals"))
                    InsertMappedRows(db, "thread_goals", "thread_id", "thread_id");
                if (TableExists(db, "main", "thread_goal_continuation_deferrals") && TableExists(db, "codexport_source", "thread_goal_continuation_deferrals"))
                    InsertMappedRows(db, "thread_goal_continuation_deferrals", "thread_id", "thread_id");
                Execute(db, "COMMIT;");
                transaction = false;
                Execute(db, "DETACH DATABASE codexport_source;");
                attached = false;
                ValidateOpenDatabase(db);
                Execute(db, "PRAGMA wal_checkpoint(TRUNCATE);");
            }
            catch
            {
                if (transaction) { try { Execute(db, "ROLLBACK;"); } catch { } }
                if (attached) { try { Execute(db, "DETACH DATABASE codexport_source;"); } catch { } }
                throw;
            }
            finally { sqlite3_close(db); }
        }

        public static void ValidateDatabase(string path)
        {
            IntPtr db = Open(path);
            try { ValidateOpenDatabase(db); }
            finally { sqlite3_close(db); }
        }

        private static IntPtr Open(string path)
        {
            IntPtr db;
            int result = sqlite3_open16(path, out db);
            if (result != 0 || db == IntPtr.Zero) throw new IOException("无法打开聊天索引数据库，SQLite 错误 " + result + "：" + path);
            return db;
        }

        private static void CreateMappingTable(IntPtr db, List<ThreadImportPlan> plans)
        {
            Execute(db, "DROP TABLE IF EXISTS temp.codexport_map; CREATE TEMP TABLE codexport_map (old_id TEXT PRIMARY KEY, new_id TEXT NOT NULL, should_add INTEGER NOT NULL, is_conflict INTEGER NOT NULL, rollout_path TEXT, archived INTEGER NOT NULL);");
            foreach (var plan in plans)
            {
                string rollout = plan.ShouldAdd ? plan.DestinationPath : string.Empty;
                Execute(db, "INSERT INTO codexport_map(old_id,new_id,should_add,is_conflict,rollout_path,archived) VALUES (" +
                    SqlLiteral(plan.OriginalId) + "," + SqlLiteral(plan.EffectiveId) + "," + (plan.ShouldAdd ? "1" : "0") + "," +
                    (plan.IsConflict ? "1" : "0") + "," + SqlLiteral(rollout) + "," + (plan.Archived ? "1" : "0") + ");");
            }
        }

        private static void InsertSections(IntPtr db)
        {
            List<SqliteColumn> columns = GetCompatibleColumns(db, "thread_sections", false);
            string names = string.Join(",", columns.Select(c => QuoteIdentifier(c.Name)));
            string values = string.Join(",", columns.Select(c => "s." + QuoteIdentifier(c.Name)));
            Execute(db, "INSERT INTO thread_sections (" + names + ") SELECT " + values + " FROM codexport_source.thread_sections s " +
                "WHERE EXISTS (SELECT 1 FROM codexport_source.threads t JOIN codexport_map m ON m.old_id=t.id WHERE m.should_add=1 AND t.thread_section_id=s.id) " +
                "AND NOT EXISTS (SELECT 1 FROM thread_sections d WHERE d.id=s.id);");
        }

        private static void InsertThreads(IntPtr db)
        {
            List<SqliteColumn> columns = GetCompatibleColumns(db, "threads", true);
            string names = string.Join(",", columns.Select(c => QuoteIdentifier(c.Name)));
            var expressions = new List<string>();
            foreach (var column in columns)
            {
                if (string.Equals(column.Name, "id", StringComparison.OrdinalIgnoreCase)) expressions.Add("m.new_id");
                else if (string.Equals(column.Name, "rollout_path", StringComparison.OrdinalIgnoreCase)) expressions.Add("m.rollout_path");
                else if (string.Equals(column.Name, "archived", StringComparison.OrdinalIgnoreCase)) expressions.Add("m.archived");
                else if (string.Equals(column.Name, "title", StringComparison.OrdinalIgnoreCase)) expressions.Add("CASE WHEN m.is_conflict=1 THEN s.title || '（导入副本）' ELSE s.title END");
                else expressions.Add("s." + QuoteIdentifier(column.Name));
            }
            Execute(db, "INSERT INTO threads (" + names + ") SELECT " + string.Join(",", expressions) +
                " FROM codexport_source.threads s JOIN codexport_map m ON m.old_id=s.id WHERE m.should_add=1;");
        }

        private static void InsertMappedRows(IntPtr db, string table, string sourceIdColumn, string targetIdColumn)
        {
            List<SqliteColumn> columns = GetCompatibleColumns(db, table, false);
            string names = string.Join(",", columns.Select(c => QuoteIdentifier(c.Name)));
            string values = string.Join(",", columns.Select(c => string.Equals(c.Name, targetIdColumn, StringComparison.OrdinalIgnoreCase) ? "m.new_id" : "s." + QuoteIdentifier(c.Name)));
            Execute(db, "INSERT INTO " + QuoteIdentifier(table) + " (" + names + ") SELECT " + values + " FROM codexport_source." + QuoteIdentifier(table) +
                " s JOIN codexport_map m ON m.old_id=s." + QuoteIdentifier(sourceIdColumn) + " WHERE m.should_add=1;");
        }

        private static void InsertSpawnEdges(IntPtr db)
        {
            List<SqliteColumn> columns = GetCompatibleColumns(db, "thread_spawn_edges", false);
            string names = string.Join(",", columns.Select(c => QuoteIdentifier(c.Name)));
            var values = new List<string>();
            foreach (var column in columns)
            {
                if (string.Equals(column.Name, "child_thread_id", StringComparison.OrdinalIgnoreCase)) values.Add("m.new_id");
                else if (string.Equals(column.Name, "parent_thread_id", StringComparison.OrdinalIgnoreCase))
                    values.Add("COALESCE((SELECT new_id FROM codexport_map p WHERE p.old_id=s.parent_thread_id),s.parent_thread_id)");
                else values.Add("s." + QuoteIdentifier(column.Name));
            }
            Execute(db, "INSERT INTO thread_spawn_edges (" + names + ") SELECT " + string.Join(",", values) +
                " FROM codexport_source.thread_spawn_edges s JOIN codexport_map m ON m.old_id=s.child_thread_id WHERE m.should_add=1;");
        }

        private static void RequireCompatibleTable(IntPtr db, string table, bool mandatory)
        {
            bool target = TableExists(db, "main", table);
            bool source = TableExists(db, "codexport_source", table);
            if (mandatory && (!target || !source)) throw new InvalidDataException("聊天索引数据库缺少必要表：" + table);
            if (target && source) GetCompatibleColumns(db, table, mandatory);
        }

        private static List<SqliteColumn> GetCompatibleColumns(IntPtr db, string table, bool mandatory)
        {
            List<SqliteColumn> target = GetColumns(db, "main", table);
            List<SqliteColumn> source = GetColumns(db, "codexport_source", table);
            var sourceNames = new HashSet<string>(source.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
            var common = target.Where(c => sourceNames.Contains(c.Name)).ToList();
            foreach (var column in target)
                if (!sourceNames.Contains(column.Name) && column.NotNull && string.IsNullOrEmpty(column.DefaultValue) && !column.PrimaryKey)
                    throw new InvalidDataException("两台电脑的 Codex 数据库版本不兼容，目标字段无法安全补全：" + table + "." + column.Name);
            if (mandatory && (!common.Any(c => string.Equals(c.Name, "id", StringComparison.OrdinalIgnoreCase)) || !common.Any(c => string.Equals(c.Name, "rollout_path", StringComparison.OrdinalIgnoreCase))))
                throw new InvalidDataException("两台电脑的 Codex 聊天索引结构不兼容。");
            if (common.Count == 0) throw new InvalidDataException("两台电脑的 Codex 数据表没有可合并字段：" + table);
            return common;
        }

        private static List<SqliteColumn> GetColumns(IntPtr db, string schema, string table)
        {
            var rows = Query(db, "PRAGMA " + QuoteIdentifier(schema) + ".table_info(" + QuoteIdentifier(table) + ");");
            return rows.Select(row => new SqliteColumn
            {
                Name = row.ContainsKey("name") ? row["name"] : string.Empty,
                NotNull = row.ContainsKey("notnull") && row["notnull"] == "1",
                DefaultValue = row.ContainsKey("dflt_value") ? row["dflt_value"] : null,
                PrimaryKey = row.ContainsKey("pk") && row["pk"] != "0"
            }).Where(c => !string.IsNullOrWhiteSpace(c.Name)).ToList();
        }

        private static bool TableExists(IntPtr db, string schema, string table)
        {
            return Query(db, "SELECT name FROM " + QuoteIdentifier(schema) + ".sqlite_master WHERE type='table' AND name=" + SqlLiteral(table) + " LIMIT 1;").Count > 0;
        }

        private static void ValidateOpenDatabase(IntPtr db)
        {
            var integrity = Query(db, "PRAGMA integrity_check;");
            if (integrity.Count != 1 || !integrity[0].Values.Any(v => string.Equals(v, "ok", StringComparison.OrdinalIgnoreCase)))
                throw new IOException("SQLite 完整性检查失败。");
            var foreignKeys = Query(db, "PRAGMA foreign_key_check;");
            if (foreignKeys.Count > 0) throw new IOException("SQLite 外键检查失败，共 " + foreignKeys.Count + " 项。");
        }

        private static List<Dictionary<string, string>> Query(IntPtr db, string sql)
        {
            var rows = new List<Dictionary<string, string>>();
            SqliteCallback callback = delegate(IntPtr argument, int columnCount, IntPtr values, IntPtr names)
            {
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < columnCount; i++)
                {
                    IntPtr namePointer = Marshal.ReadIntPtr(names, i * IntPtr.Size);
                    IntPtr valuePointer = Marshal.ReadIntPtr(values, i * IntPtr.Size);
                    row[Utf8PointerToString(namePointer)] = valuePointer == IntPtr.Zero ? null : Utf8PointerToString(valuePointer);
                }
                rows.Add(row);
                return 0;
            };
            ExecuteInternal(db, sql, callback);
            GC.KeepAlive(callback);
            return rows;
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
            ExecuteInternal(db, sql, null);
        }

        private static void ExecuteInternal(IntPtr db, string sql, SqliteCallback callback)
        {
            IntPtr error;
            byte[] utf8 = Encoding.UTF8.GetBytes(sql + "\0");
            int result = sqlite3_exec_utf8(db, utf8, callback, IntPtr.Zero, out error);
            if (result == 0) return;
            string message = error == IntPtr.Zero ? "未知 SQLite 错误" : Utf8PointerToString(error);
            if (error != IntPtr.Zero) sqlite3_free(error);
            throw new InvalidOperationException("SQLite 错误 " + result + "：" + message);
        }

        private static string Utf8PointerToString(IntPtr pointer)
        {
            if (pointer == IntPtr.Zero) return null;
            int length = 0;
            while (Marshal.ReadByte(pointer, length) != 0) length++;
            byte[] bytes = new byte[length];
            Marshal.Copy(pointer, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }

        private static string SqlLiteral(string value)
        {
            return value == null ? "NULL" : "'" + value.Replace("'", "''") + "'";
        }

        private static string QuoteIdentifier(string value)
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private sealed class SqliteColumn
        {
            public string Name { get; set; }
            public bool NotNull { get; set; }
            public string DefaultValue { get; set; }
            public bool PrimaryKey { get; set; }
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
        public string SourceCodexHome { get; set; }
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
        public int ExistingThreads { get; set; }
        public int AddedThreads { get; set; }
        public int SkippedDuplicates { get; set; }
        public int ConflictCopies { get; set; }
        public int TotalThreads { get; set; }
        public int AddedResources { get; set; }
        public string BackupPath { get; set; }
    }

    internal sealed class ThreadFileInfo
    {
        public string Id { get; set; }
        public string SourcePath { get; set; }
        public string RelativePath { get; set; }
        public bool Archived { get; set; }
        public string Fingerprint { get; set; }
    }

    internal sealed class ThreadImportPlan
    {
        public string OriginalId { get; set; }
        public string EffectiveId { get; set; }
        public string SourcePath { get; set; }
        public string SourceRelativePath { get; set; }
        public string DestinationRelativePath { get; set; }
        public string DestinationPath { get; set; }
        public string Fingerprint { get; set; }
        public bool Archived { get; set; }
        public bool ShouldAdd { get; set; }
        public bool IsConflict { get; set; }
    }

    internal sealed class ResourceImportPlan
    {
        public string SourcePath { get; set; }
        public string DestinationRelativePath { get; set; }
        public string Sha256 { get; set; }
        public bool ShouldCopy { get; set; }
    }

    internal sealed class MergePlan
    {
        public MergePlan()
        {
            IdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Threads = new List<ThreadImportPlan>();
        }
        public string SourceCodexHome { get; set; }
        public Dictionary<string, string> IdMap { get; private set; }
        public List<ThreadImportPlan> Threads { get; private set; }
        public int ExistingThreads { get; set; }
        public int AddedThreads { get; set; }
        public int SkippedDuplicates { get; set; }
        public int ConflictCopies { get; set; }
        public int AddedResources { get; set; }
    }
}
