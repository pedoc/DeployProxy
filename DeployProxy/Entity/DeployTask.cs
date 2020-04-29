using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DeployProxy.Controllers;
using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using Microsoft.Extensions.Logging;
using ShellProgressBar;

namespace DeployProxy.Entity
{
    public class DeployTask
    {
        private static ConcurrentDictionary<string, DeployTask> _running = new ConcurrentDictionary<string, DeployTask>();

        private const string Config = "deployTasks.json";
        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsEnable { get; set; }
        /// <summary>
        /// 工程名称
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// 本地仓库存储目录
        /// </summary>
        public string LocalDirectory { get; set; }
        /// <summary>
        /// 发布目录
        /// </summary>
        public string PublishDirectory { get; set; }
        /// <summary>
        /// 分支名称
        /// </summary>
        public string Branch { get; set; } = "master";
        /// <summary>
        /// 用户名
        /// </summary>
        public string Username { get; set; }
        /// <summary>
        /// 密码
        /// </summary>
        public string Password { get; set; }
        /// <summary>
        /// 电子邮件
        /// </summary>
        public string Email { get; set; }
        /// <summary>
        /// 执行脚本
        /// </summary>
        public string Script { get; set; }

        public static List<DeployTask> GetDeployTasks()
        {
            if (!File.Exists(Config)) return null;
            var content = File.ReadAllText(Config, Encoding.UTF8);
            if (string.IsNullOrEmpty(content)) return null;
            return content
                .FromJson<List<DeployTask>>()
                ?.Where(i => i.IsEnable)
                .ToList();
        }

        internal void Ensure()
        {
            if (!Directory.Exists(PublishDirectory)) Directory.CreateDirectory(PublishDirectory);
        }

        private string GetKey()
        {
            return $"{Name}-{Branch}";
        }

        public Task Execute(ILogger logger, Event @event)
        {
            var key = GetKey();
            if (_running.ContainsKey(key))
            {
                logger?.LogWarning($"{key} 正在处理中，跳过本次任务");
                return Task.CompletedTask;
            }
            _running.TryAdd(key, this);
            try
            {
                var repoAddress = @event.project.http_url;
                if (string.IsNullOrEmpty(repoAddress)) return Task.CompletedTask; ;
                CredentialsHandler credentialsProvider = null;
                if (!string.IsNullOrEmpty(Username))
                    credentialsProvider = new CredentialsHandler(
                        (url, usernameFromUrl, types) =>
                            new UsernamePasswordCredentials()
                            {
                                Username = Username,
                                Password = Password
                            });
                const int totalTicks = 0;
                var pbar = new ProgressBar(totalTicks, "", new ProgressBarOptions
                {
                    ProgressCharacter = '─',
                    ProgressBarOnBottom = true
                });
                if (!Directory.Exists(LocalDirectory))
                {
                    logger?.LogDebug($"正在clone {repoAddress}到 {LocalDirectory}");
                    LibGit2Sharp.Repository.Clone(repoAddress, LocalDirectory,
                        new CloneOptions()
                        {
                            BranchName = Branch,
                            CredentialsProvider = credentialsProvider,
                            OnTransferProgress = (t) =>
                            {
                                pbar.MaxTicks = t.TotalObjects;
                                pbar.Tick(t.ReceivedObjects, $"Key={key} LocalDirectory={LocalDirectory} RepoAddress={repoAddress}");
                                return true;
                            }
                        });
                }
                if (!LibGit2Sharp.Repository.IsValid(LocalDirectory))
                {
                    ResetGitRepo(logger);
                       logger?.LogError($"{key} 的仓库目录 {LocalDirectory} 不是有效git仓库");
                    return Task.CompletedTask;
                }
                using var repo = new LibGit2Sharp.Repository(LocalDirectory);
                var branch = repo.Branches[Branch];

                if (branch == null)
                {
                    ResetGitRepo(logger);
                    logger?.LogError($"{key} 的仓库分支 {Branch} 不存在");
                    return Task.CompletedTask;
                }
                logger?.LogDebug($"正在checkout {Branch}");
                Commands.Checkout(repo, branch);
                logger?.LogDebug($"正在pull {Branch}");
                pbar.Dispose();
                pbar = new ProgressBar(totalTicks, $"pull {Branch}", new ProgressBarOptions
                {
                    ProgressCharacter = '─',
                    ProgressBarOnBottom = true
                });
                var options = new LibGit2Sharp.PullOptions
                {
                    FetchOptions = new FetchOptions()
                    {
                        OnTransferProgress = (t) =>
                        {
                            pbar.MaxTicks = t.TotalObjects;
                            pbar.Tick(t.ReceivedObjects, $"Key={key} LocalDirectory={LocalDirectory} RepoAddress={repoAddress}");
                            return true;
                        }
                    }
                };
                if (!string.IsNullOrEmpty(Username))
                    options.FetchOptions.CredentialsProvider = credentialsProvider;

                var signature = new LibGit2Sharp.Signature(new Identity(Username, Email), DateTimeOffset.Now);
                var mergeResult = Commands.Pull(repo, signature, options);
                if (mergeResult.Status != MergeStatus.UpToDate)
                {
                    logger?.LogError($"仓库拉取失败，状态:{mergeResult.Status}");
                    return Task.CompletedTask;
                }
                var script = Script;
                if (string.IsNullOrEmpty(script)) script = $"dotnet publish -c Release -o {PublishDirectory}";
                var result = ExecuteCmd(script, LocalDirectory, logger);
                logger?.LogDebug($"发布命令执行{(result ? "成功" : "失败")}");
                GC.KeepAlive(pbar);
                pbar.Dispose();
            }
            finally
            {
                _running.Remove(key, out _);
            }
            return Task.CompletedTask;
        }

        private void ResetGitRepo(ILogger logger)
        {
            var gitDirectory = Path.Combine(LocalDirectory, ".git");
            if (Directory.Exists(gitDirectory)&&Directory.EnumerateDirectories(LocalDirectory).Count()==1)
            {
                try
                {
                    logger?.LogDebug($"{LocalDirectory} 下仅存在 .git 目录，将移除此冗余文件以便可重新初始化");
                    Directory.Delete(gitDirectory, true);
                }
                catch (Exception ex)
                {
                    logger?.LogError($"{LocalDirectory} 下仅存在 .git 目录，但无法自动清理，错误={ex.Message}");
                }
            }
        }
        private bool ExecuteCmd(string cmd, string workingDirectory, ILogger logger, string exe="dotnet.exe")
        {
            try
            {
                if (string.IsNullOrEmpty(workingDirectory)) workingDirectory = Environment.CurrentDirectory;
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = exe,
                        Arguments = cmd,
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = workingDirectory
                    },
                    EnableRaisingEvents = true
                };
                process.ErrorDataReceived += (s, e) => logger?.LogError(e.Data);
                process.OutputDataReceived += (s, e) =>logger?.LogInformation(e.Data);
                process.Start();
                logger?.LogInformation($"正在执行脚本({process.Id}) {cmd} {Environment.NewLine}");
                process.BeginOutputReadLine();
                process.WaitForExit();
                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }
    }
}
