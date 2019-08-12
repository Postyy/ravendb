﻿using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Raven.Client.Documents.Operations.Backups;
using Raven.Server.Documents.PeriodicBackup.Azure;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Documents.PeriodicBackup.Restore
{
    class AzureRestorePoints : RestorePointsBase
    {
        private readonly RavenAzureClient _client;
        public AzureRestorePoints(SortedList<DateTime, RestorePoint> sortedList, TransactionOperationContext context, AzureSettings azureSettings) : base(sortedList, context)
        {
            _client = new RavenAzureClient(azureSettings);
        }

        public override async Task FetchRestorePoints(string path)
        {
            var objects = await _client.ListBlobs(path, "/", true);
            var folders = objects.ListBlob.ToList();

            if (folders.Count == 0)
            {
                await FetchRestorePointsForPath(path, assertLegacyBackups: true);
            }
            else
            {
                foreach (var folder in folders)
                {
                    await FetchRestorePointsForPath(folder.Name, assertLegacyBackups: true);
                }
            }
        }

        protected override async Task<List<FileInfoDetails>> GetFiles(string path)
        {
            var allObjects = await _client.ListBlobs(path ,string.Empty, false);

            var filesInfo = new List<FileInfoDetails>();

            foreach (var obj in allObjects.ListBlob)
            {
                if (TryExtractDateFromFileName(obj.Name, out var lastModified) == false)
                    lastModified = Convert.ToDateTime(obj.LastModify);

                filesInfo.Add(new FileInfoDetails
                {
                    FullPath = obj.Name,
                    LastModified = lastModified
                });
            }

            return filesInfo;
        }

        protected override ParsedBackupFolderName ParseFolderNameFrom(string path)
        {
            var arr = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var lastFolderName = arr.Length > 0 ? arr[arr.Length - 1] : string.Empty;

            return ParseFolderName(lastFolderName);
        }

        protected override async Task<ZipArchive> GetZipArchive(string filePath)
        {
            var blob = await _client.GetBlob(filePath);
            return new ZipArchive(blob.Data, ZipArchiveMode.Read);
        }

        protected override string GetFileName(string fullPath)
        {
            return fullPath.Split('/').Last();
        }

        public override void Dispose()
        {
            _client.Dispose();
        }
    }
}
