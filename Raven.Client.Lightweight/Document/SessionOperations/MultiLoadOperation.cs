﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Raven.Abstractions.Data;
using Raven.Abstractions.Extensions;
using Raven.Abstractions.Logging;
using Raven.Client.Connection;

namespace Raven.Client.Document.SessionOperations
{
	public class MultiLoadOperation
	{
		private static readonly ILog log = LogManager.GetCurrentClassLogger();

		private readonly InMemoryDocumentSessionOperations sessionOperations;
		internal Func<IDisposable> disableAllCaching { get; set; }
		private readonly string[] ids;
		private readonly KeyValuePair<string, Type>[] includes;
		bool firstRequest = true;
		JsonDocument[] results;
		JsonDocument[] includeResults;

		private Stopwatch sp;

		public MultiLoadOperation(InMemoryDocumentSessionOperations sessionOperations, Func<IDisposable> disableAllCaching, string[] ids, KeyValuePair<string, Type>[] includes)
		{
			this.sessionOperations = sessionOperations;
			this.disableAllCaching = disableAllCaching;
			this.ids = ids;
			this.includes = includes;
		}

		public void LogOperation()
		{
			if (ids == null)
				return;

			log.Debug("Bulk loading ids [{0}] from {1}", string.Join(", ", ids), sessionOperations.StoreIdentifier);
		}

		public IDisposable EnterMultiLoadContext()
		{
			if (firstRequest == false) // if this is a repeated request, we mustn't use the cached result, but have to re-query the server
				return disableAllCaching();
			sp = Stopwatch.StartNew();

			return null;
		}

		public bool SetResult(MultiLoadResult multiLoadResult)
		{
			firstRequest = false;
			includeResults = SerializationHelper.RavenJObjectsToJsonDocuments(multiLoadResult.Includes).ToArray();
			results = SerializationHelper.RavenJObjectsToJsonDocuments(multiLoadResult.Results).ToArray();

			return sessionOperations.AllowNonAuthoritativeInformation == false &&
					results.Where(x => x != null).Any(x => x.NonAuthoritativeInformation ?? false) &&
					sp.Elapsed < sessionOperations.NonAuthoritativeInformationTimeout
				;
		}

		public T[] Complete<T>()
		{
			for (var i = 0; i < includeResults.Length; i++)
			{
				var include = includeResults[i];
				sessionOperations.TrackIncludedDocument(include);
			}

			var finalResults = ids != null ? 
				ReturnResultsById<T>() : 
				ReturnResults<T>();
			for (var i = 0; i < finalResults.Length; i++)
			{
				var finalResult = finalResults[i];
				if (ReferenceEquals(finalResult, null))
					sessionOperations.RegisterMissing(ids[i]);
			}

			var includePaths = includes != null ? includes.Select(x => x.Key).ToArray() : null;
			sessionOperations.RegisterMissingIncludes(results.Where(x => x != null).Select(x => x.DataAsJson), includePaths);

			return finalResults;
		}

		private T[] ReturnResults<T>()
		{
			var finalResults = new T[results.Length];
			for (int i = 0; i < results.Length; i++)
			{
				if (results[i] != null)
					finalResults[i] = sessionOperations.TrackEntity<T>(results[i]);
			}
			return finalResults;
		}

		private T ApplyTrackingIfNeeded<T>(JsonDocument document)
		{
			if (document != null)
				return sessionOperations.TrackEntity<T>(document);

			return default(T);
		}

		private T[] ReturnResultsById<T>()
		{
			var finalResults = new T[ids.Length];
			var dic = new Dictionary<string, int>(ids.Length, StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < ids.Length; i++)
			{
				if (ids[i] == null)
					continue;
				dic[ids[i]] = i;
			}
			foreach (var jsonDocument in results)
			{
				if (jsonDocument == null)
					continue;
				var id = jsonDocument.Metadata.Value<string>("@id");
				int value;
				if (dic.TryGetValue(id, out value))
					finalResults[value] = sessionOperations.TrackEntity<T>(jsonDocument);
			}
			return finalResults;
		}
	}
}