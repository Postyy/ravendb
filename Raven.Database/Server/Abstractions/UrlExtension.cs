//-----------------------------------------------------------------------
// <copyright file="UrlExtension.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using Raven.Database.Config;

namespace Raven.Database.Server.Abstractions
{
    public static class UrlExtension
    {
        public static string GetRequestUrlFromRawUrl(string rawUrl, RavenConfiguration configuration)
        {
            string localPath = rawUrl;
            var indexOfQuery = localPath.IndexOf('?');
            if (indexOfQuery != -1)
                localPath = localPath.Substring(0, indexOfQuery);
            if (localPath.StartsWith("//"))
                localPath = localPath.Substring(1);
            if (configuration.Core.VirtualDirectory != "/" &&
                localPath.StartsWith(configuration.Core.VirtualDirectory, StringComparison.OrdinalIgnoreCase))
            {
                localPath = localPath.Substring(configuration.Core.VirtualDirectory.Length);
                if (localPath.Length == 0)
                    localPath = "/";
            }
            return localPath;
        }
    }
}
