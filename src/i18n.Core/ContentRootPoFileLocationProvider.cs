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
    /// provides a localization files from the content root folder.
    /// </summary>
    public class ContentRootPoFileLocationProvider : DirectoryPoFileLocationProvider
    {
        /// <summary>
        /// Creates a new instance of <see cref="ContentRootPoFileLocationProvider"/>.
        /// </summary>
        /// <param name="hostEnvironment"><see cref="IHostEnvironment"/>.</param>
        /// <param name="requestLocalizationOptions">The IOptions<RequestLocalizationOptions>.</param>
        public ContentRootPoFileLocationProvider(IHostEnvironment hostEnvironment, IOptions<RequestLocalizationOptions> requestLocalizationOptions) :
            base(Path.Combine(hostEnvironment.ContentRootPath, "locale"), requestLocalizationOptions.Value.DefaultRequestCulture.Culture.Name)
        {
        }
    }
}
