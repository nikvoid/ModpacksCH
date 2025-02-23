﻿using ModpacksCH.API;
using ModpacksCH.API.Model;
using ModpacksCH.Models;
using System.Diagnostics;

namespace ModpacksCH
{
    public abstract class ModpackDownloader : IDisposable
    {
        protected readonly DownloadInfo Info;
        protected readonly SemaphoreSlim Semaphore;
        protected int Count;
        private const string CDNEndpoint = "https://edge.forgecdn.net/files";
        private readonly CFClient CF = new();
        private readonly HttpClient Client = new();

        protected ModpackDownloader(DownloadInfo info) : this(info, 4) { }

        protected ModpackDownloader(DownloadInfo info, int threads)
        {
            Info = info;
            Semaphore = new(threads);
        }

        public static ModpackDownloader Create(DownloadInfo info)
        {
            return info.Version switch
            {
                CHVersion => new CHDownloader(info),
                CFVersion => new CFDownloader(info),
                _ => throw new NotImplementedException()
            };
        }

        public virtual Task<string> Download(string path) => Download(path, default);

        public virtual async Task<string> Download(string path, IProgress<int> IP)
        {
            var Files = Info.Files;
            Trace.WriteLine($"Download started: {Files.Count} files");
            var Tasks = Files.Select(async (F) =>
            {
                try
                {
                    await DownloadFile(path, F);
                }
                catch (Exception ex)
                {
                    var line = $"Failed to download file: {F.Name} ({F.Url})";
                    Console.WriteLine(line); 
                    Console.WriteLine(ex);
                    Trace.WriteLine(line);
                    return;
                }
                
                Interlocked.Increment(ref Count);
                IP?.Report(Count);
            });
            await Task.WhenAll(Tasks);
            IP.Report(Count);
            Trace.WriteLine($"Download done: {Files.Count} files");
            // TODO Add hash check
            return path;
        }

        protected async Task<string> DownloadFile(string path, ModpackFile file)
        {
            await Semaphore.WaitAsync();

            var LocalFile = new FileInfo(Path.Combine(path, file.Path, file.Name));
            var URL = file.Url;
            if (string.IsNullOrEmpty(URL))
            {
                var File = (await CF.GetModFile(file.CurseForge.Project, file.CurseForge.File)).Data;
                URL = File.DownloadURL;
                //IDK Why url is null; Controlled by mod author?
                if (URL is null)
                {
                    // It just works
                    var ID = file.CurseForge.File.ToString();
                    URL = $"{CDNEndpoint}/{ID[..4]}/{ID[4..]}/{File.FileName}";
                }
            }

            Trace.WriteLine($"Downloading file: {URL} ");
            using var Responce = await Client.GetAsync(URL);
            Responce.EnsureSuccessStatusCode();
            Directory.CreateDirectory(LocalFile.DirectoryName);
            using (var FS = new FileStream(LocalFile.FullName, FileMode.Create))
            {
                await Responce.Content.CopyToAsync(FS);
            }

            Semaphore.Release();
            return LocalFile.FullName;
        }

        #region IDispose

        public void Dispose()
        {
            ((IDisposable)Client).Dispose();
            ((IDisposable)CF).Dispose();
            GC.SuppressFinalize(this);
        }

        #endregion IDispose
    }
}