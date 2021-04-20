# i18n.Core: Smart internationalization for ASP.NET Core

This is a fork of [https://github.com/fintermobilityas/i18n.core]() that is not published on Nuget. Adds some functionality such as:

- projecting translated files
- building POT without merging into PO
- adds formatting fragment (e.g. [[[For %0 do %1|||Alice|||Bob]]])


### Platforms supported

- ASP.NET Core 3.1 

### Introduction

The i18n library is designed to replace the use of .NET resources in favor 
of an **easier**, globally recognized standard for localizing ASP.NET-based web applications.

### Project configuration (ASP.NET CORE)

```xml
<PackageReference Include="i18n.Core" Version="1.0.10" />
```

```cs
// This method gets called by the runtime. Use this method to add services to the container.
public void ConfigureServices(IServiceCollection services)
{
    services.AddI18NLocalization(HostEnvironment, options =>
    {
        var supportedCultures = new[]
        {
            new CultureInfo("nb-NO"),
            new CultureInfo("en-US")
        };

        var defaultCulture = supportedCultures.Single(x => x.Name == "en-US");

        options.DefaultRequestCulture = new RequestCulture(defaultCulture);
        options.SupportedCultures = supportedCultures;
        options.SupportedUICultures = supportedCultures;
        options.RequestCultureProviders = new List<IRequestCultureProvider>
        {
            new CookieRequestCultureProvider()
        };
    });
}
```

```cs
// This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
{
    app.UseI18NRequestLocalization();
}
```

### Install pot  CLI tool

The CLI tool allows running certain translations processes from the command line. This is useful to add to your csproj as a BeforeBuild step to ensure your POT (and optionally PO) files are always in sync with your source code.

If you build and compile to your own Nuget repository, you can use this command to install it globally:

```
dotnet tool install pot -g
```

Otherwise use the full path to the POT.exe in the following examples.

#### Create or update pot file 

```
pot
```

This will scan your files for Nuggets and add them to the main POT file and merge any changes into the language PO files. If you only want to update the POT file, but not merge changes into PO files pass the following argument:

```
pot --build-no-merge
```

#### Automatically update pot files when files change

```
pot --watch
```

or

```
pot --watch --build-no-merge
```

#### Generate localized outputs from .pot files

The project command is useful when you need to create different localized files to upload to somewhere. For example mail templates that don't support I18N natively.

```
pot --project .\templates\ --project-default-lang en-US
```

Specify the template directory and the default language that corresponds to your messages.pot file. The remaining configuration will be read from the web.config file.

### Custom configuration (Web.config)

NB! This is not required for this to work as you can configure this middleware by resolving `IOptions<I18NLocalizationOptions>`. It's available for legacy reasons only.

```xml
<?xml version="1.0"?>

<configuration>
  <appSettings>
    <add key="i18n.DirectoriesToScan" value=".;"/>
    <add key="i18n.GenerateTemplatePerFile" value="false"/>
  </appSettings>
</configuration>
```

### Demo

A demo project is available in this repository. You can find it [here](https://github.com/fintermobilityas/i18n.core/tree/master/src/i18n.Demo)

### Special thanks to

This project is a fork of:

- https://github.com/fintermobilityas/i18n.core


This project is mainly built on hard work of the following projects:

- https://github.com/OrchardCMS/OrchardCore
- https://github.com/turquoiseowl/i18n
