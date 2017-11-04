#tool "nuget:?package=GitVersion.CommandLine"

///////////////////////////////////////////////////////////////////////////////
// ARGUMENTS
///////////////////////////////////////////////////////////////////////////////

var target = Argument("Target", "Default");
var tag = Argument("Tag", "cake");

var configuration = HasArgument("Configuration")
    ? Argument<string>("Configuration")
    : EnvironmentVariable("CONFIGURATION") != null
        ? EnvironmentVariable("CONFIGURATION")
        : "Release";

var buildNumber = HasArgument("BuildNumber")
    ? Argument<int>("BuildNumber")
    : AppVeyor.IsRunningOnAppVeyor
        ? AppVeyor.Environment.Build.Number
        : TravisCI.IsRunningOnTravisCI
            ? TravisCI.Environment.Build.BuildNumber
            : EnvironmentVariable("BUILD_NUMBER") != null
                ? int.Parse(EnvironmentVariable("BUILD_NUMBER"))
                : 0;

///////////////////////////////////////////////////////////////////////////////
// GLOBALS
///////////////////////////////////////////////////////////////////////////////

var isAppVeyor = AppVeyor.IsRunningOnAppVeyor;
var isTravis = TravisCI.IsRunningOnTravisCI;
var isCi = isAppVeyor || isTravis;

///////////////////////////////////////////////////////////////////////////////
// SETUP / TEARDOWN
///////////////////////////////////////////////////////////////////////////////

var projects = new[] {
    "./src/Corale.Colore/Corale.Colore.csproj",
    "./src/Corale.Colore.Tests/Corale.Colore.Tests.csproj"
};
var mainProject = projects[0];
var frameworks = new List<string>();

GitVersion version = null;

Setup(ctx =>
{
    Information("Reading framework settings");

    var xmlValue = XmlPeek(mainProject, "/Project/PropertyGroup/TargetFrameworks");
    if (string.IsNullOrEmpty(xmlValue))
    {
        xmlValue = XmlPeek(mainProject, "/Project/PropertyGroup/TargetFramework");
    }

    frameworks.AddRange(xmlValue.Split(';'));

    Information("Frameworks: {0}", string.Join(", ", frameworks));

    version = GitVersion(new GitVersionSettings
    {
        RepositoryPath = ".",
        OutputType = isCi ? GitVersionOutput.BuildServer : GitVersionOutput.Json
    });

    Information("Version: {0} on {1}", version.FullSemVer, version.CommitDate);
    Information("Commit hash: {0}", version.Sha);
});

Teardown(ctx =>
{
	// Executed AFTER the last task.
	Information("Finished running tasks.");
});

///////////////////////////////////////////////////////////////////////////////
// TASKS
///////////////////////////////////////////////////////////////////////////////

Task("Clean")
    .Does(() =>
    {
        CleanDirectory("./artifacts");
        CleanDirectory("./publish");
    });

Task("Restore")
    .IsDependentOn("Clean")
    .Does(() =>
    {
        DotNetCoreRestore("src/");
    });

Task("Build")
    .IsDependentOn("Restore")
    .Does(() =>
    {
        foreach (var project in projects)
        {
            DotNetCoreBuild(
                project,
                new DotNetCoreBuildSettings
                {
                    Configuration = configuration,
                    ArgumentCustomization = args => args
                        .Append($"/p:AssemblyVersion={version.AssemblySemVer}")
                        .Append($"/p:NuGetVersion={version.NuGetVersionV2}")
                });
        }
    });

Task("Test")
    .IsDependentOn("Build")
    .Does(() =>
    {
        var settings = new DotNetCoreTestSettings
        {
            Configuration = configuration,
            NoBuild = true
        };

        if (AppVeyor.IsRunningOnAppVeyor)
        {
            settings.ArgumentCustomization = args => args
                .Append("--logger:AppVeyor");
        }
        else
        {
            settings.ArgumentCustomization = args => args
                .Append("--logger:nunit");
        }

        DotNetCoreTest(
            "src/Corale.Colore.Tests/Corale.Colore.Tests.csproj",
            settings);

        if (AppVeyor.IsRunningOnAppVeyor)
        {
            return;
        }

        var testResults = GetFiles("src/Corale.Colore.Tests/TestResults/*.xml");
        CopyFiles(testResults, "./artifacts");
    });

Task("Dist")
    .IsDependentOn("Test")
    .Does(() =>
    {
        foreach (var framework in frameworks)
        {
            var dir = $"./src/Corale.Colore/bin/{configuration}/{framework}/";
            var target = $"./artifacts/colore_{version.SemVer}_{framework}_anycpu.zip";
            Information("Zipping {0} to {1}", dir, target);
            Zip(dir, target, $"{dir}**/*.*");
        }
    });

Task("Publish")
    .IsDependentOn("Test")
    .Does(() =>
    {
        foreach (var framework in frameworks)
        {
            var settings = new DotNetCorePublishSettings
            {
                Framework = framework,
                Configuration = configuration,
                OutputDirectory = $"./publish/{framework}/",
                ArgumentCustomization = args => args
                    .Append($"/p:AssemblyVersion={version.AssemblySemVer}")
                    .Append($"/p:NuGetVersion={version.NuGetVersionV2}")
            };

            DotNetCorePublish("src/Corale.Colore", settings);

            var dir = $"./publish/{framework}/";
            var target = $"./artifacts/colore_{version.SemVer}_{framework}_full.zip";
            Information("Zipping {0} to {1}", dir, target);
            Zip(dir, target, $"{dir}**/*.*");
        }
    });

Task("Pack")
    .IsDependentOn("Test")
    .Does(() =>
    {
        MoveFiles(GetFiles("./src/**/*.nupkg"), "./artifacts");
    });

Task("CI")
    .IsDependentOn("Dist")
    .IsDependentOn("Publish")
    .IsDependentOn("Pack");

Task("Default")
    .IsDependentOn("Test");

RunTarget(target);