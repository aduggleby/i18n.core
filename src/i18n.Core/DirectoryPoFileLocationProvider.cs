using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using i18n.Core.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace i18n.Core
{
    /// <summary>
    /// provides a localization files from a folder.
    /// </summary>
    public class DirectoryPoFileLocationProvider : ILocalizationFileLocationProvider
    {
        readonly string _directoryPath;
        readonly string _defaultCultureName;

        /// <summary>
        /// Creates a new instance of <see cref="DirectoryPoFileLocationProvider"/>.
        /// </summary>
        /// <param name="hostEnvironment"><see cref="IHostEnvironment"/>.</param>
        /// <param name="requestLocalizationOptions">The IOptions<RequestLocalizationOptions>.</param>
        public DirectoryPoFileLocationProvider(string directoryPath, string defaultCultureName)
        {
            _directoryPath = directoryPath;
            _defaultCultureName = defaultCultureName;
        }

        /// <inheritdocs />
        public IEnumerable<IFileInfo> GetLocations(string cultureName)
        {
            if (string.Equals(cultureName, _defaultCultureName, StringComparison.Ordinal))
            {
                yield return new PhysicalFileInfo(new FileInfo(Path.Combine(_directoryPath,  "messages.pot")));
                yield break;
            }

            yield return new PhysicalFileInfo(new FileInfo(Path.Combine(_directoryPath, cultureName, "messages.po")));
        }
    }
}
