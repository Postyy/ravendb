﻿// -----------------------------------------------------------------------
//  <copyright file="VersioningPutTrigger.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------
using System;
using System.ComponentModel.Composition;
using System.Linq;

using Raven.Abstractions.Data;
using Raven.Abstractions.FileSystem;
using Raven.Bundles.Versioning.Data;
using Raven.Database.FileSystem.Plugins;
using Raven.Database.FileSystem.Storage;
using Raven.Database.Plugins;
using Raven.Json.Linq;

namespace Raven.Database.FileSystem.Bundles.Versioning.Plugins
{
	[InheritedExport(typeof(AbstractFilePutTrigger))]
	[ExportMetadata("Bundle", "Versioning")]
	public class VersioningPutTrigger : AbstractFilePutTrigger
	{
		public override VetoResult AllowPut(string name, RavenJObject headers, IStorageActionsAccessor accessor)
		{
			var file = accessor.ReadFile(name);
			if (file == null)
				return VetoResult.Allowed;

			if (FileSystem.ChangesToRevisionsAllowed() == false &&
				file.Metadata.Value<string>(VersioningUtil.RavenFileRevisionStatus) == "Historical" &&
				accessor.IsVersioningActive())
			{
				return VetoResult.Deny("Modifying a historical revision is not allowed");
			}

			return VetoResult.Allowed;
		}

		public override void OnPut(string name, RavenJObject headers, IStorageActionsAccessor accessor)
		{
			VersioningConfiguration versioningConfiguration;

			if (headers.ContainsKey(Constants.RavenCreateVersion))
			{
				headers.__ExternalState[Constants.RavenCreateVersion] = headers[Constants.RavenCreateVersion];
				headers.Remove(Constants.RavenCreateVersion);
			}

			if (TryGetVersioningConfiguration(name, headers, accessor, out versioningConfiguration) == false)
				return;

			var revision = GetNextRevisionNumber(name, accessor);

			using (FileSystem.DisableAllTriggersForCurrentThread())
			{
				RemoveOldRevisions(name, revision, versioningConfiguration);
			}

			headers.__ExternalState["Next-Revision"] = revision;
			headers.__ExternalState["Parent-Revision"] = headers.Value<string>(VersioningUtil.RavenFileRevision);

			headers[VersioningUtil.RavenFileRevisionStatus] = RavenJToken.FromObject("Current");
			headers[VersioningUtil.RavenFileRevision] = RavenJToken.FromObject(revision);
		}

		public override void AfterPut(string name, long? size, RavenJObject headers, IStorageActionsAccessor accessor)
		{
			if (accessor.IsVersioningActive() == false)
				return;

			using (FileSystem.DisableAllTriggersForCurrentThread())
			{
				var copyHeaders = new RavenJObject(headers);
				copyHeaders[VersioningUtil.RavenFileRevisionStatus] = RavenJToken.FromObject("Historical");
				copyHeaders[Constants.RavenReadOnly] = true;
				copyHeaders.Remove(VersioningUtil.RavenFileRevision);
				object parentRevision;
				headers.__ExternalState.TryGetValue("Parent-Revision", out parentRevision);
				if (parentRevision != null)
				{
					copyHeaders[VersioningUtil.RavenFileParentRevision] = name + "/revisions/" + parentRevision;
				}

				object value;
				headers.__ExternalState.TryGetValue("Next-Revision", out value);

				accessor.PutFile(name + "/revisions/" + value, size, copyHeaders);
			}
		}

		public override void OnUpload(string name, RavenJObject headers, int pageId, int pagePositionInFile, int pageSize, IStorageActionsAccessor accessor)
		{
			if (accessor.IsVersioningActive() == false)
				return;

			object value;
			headers.__ExternalState.TryGetValue("Next-Revision", out value);

			accessor.AssociatePage(name + "/revisions/" + value, pageId, pagePositionInFile, pageSize);
		}

		public override void AfterUpload(string name, RavenJObject headers, IStorageActionsAccessor accessor)
		{
			if (accessor.IsVersioningActive() == false)
				return;

			object value;
			headers.__ExternalState.TryGetValue("Next-Revision", out value);

			accessor.CompleteFileUpload(name + "/revisions/" + value);
		}

		private static long GetNextRevisionNumber(string name, IStorageActionsAccessor accessor)
		{
			long revision = 1;

			var existingFile = accessor.ReadFile(name);
			if (existingFile != null)
			{
				RavenJToken existingRevisionToken;
				if (existingFile.Metadata.TryGetValue(VersioningUtil.RavenFileRevision, out existingRevisionToken))
					revision = existingRevisionToken.Value<int>() + 1;
			}
			else
			{
				var latestRevisionsFile = GetLatestRevisionsFile(name, accessor);
				if (latestRevisionsFile != null)
				{
					var id = latestRevisionsFile.Name;
					if (id.StartsWith(name, StringComparison.CurrentCultureIgnoreCase))
					{
						var revisionNum = id.Substring((name + "/revisions/").Length);
						int result;
						if (int.TryParse(revisionNum, out result))
							revision = result + 1;
					}
				}
			}

			return revision;
		}

		private static FileHeader GetLatestRevisionsFile(string name, IStorageActionsAccessor accessor)
		{
			return accessor.ReadFiles(0, int.MaxValue) // TODO [ppekrol] fix this
				.LastOrDefault(x => x.Name.StartsWith(name + "/revisions/", StringComparison.OrdinalIgnoreCase));
		}

		private void RemoveOldRevisions(string name, long revision, VersioningConfiguration versioningConfiguration)
		{
			var latestValidRevision = revision - versioningConfiguration.MaxRevisions;
			if (latestValidRevision <= 0)
				return;

			FileSystem.StorageOperationsTask.IndicateFileToDelete(string.Format("{0}/revisions/{1}", name, latestValidRevision));
		}

		private static bool TryGetVersioningConfiguration(string name, RavenJObject metadata, IStorageActionsAccessor accessor, out VersioningConfiguration versioningConfiguration)
		{
			versioningConfiguration = null;
			if (name.StartsWith("Raven/", StringComparison.OrdinalIgnoreCase))
				return false;

			if (metadata.Value<string>(VersioningUtil.RavenFileRevisionStatus) == "Historical")
				return false;

			versioningConfiguration = accessor.GetVersioningConfiguration();
			if (versioningConfiguration == null || versioningConfiguration.Exclude
				|| (versioningConfiguration.ExcludeUnlessExplicit && !metadata.__ExternalState.ContainsKey(Constants.RavenCreateVersion)))
				return false;
			return true;
		}
	}
}