#tool "nuget:?package=xunit.runner.console&version=2.4.1"
#tool "nuget:?package=GitVersion.CommandLine&version=3.6.5"

//////////////////////////////////////////////////////////////////////
// ARGUMENTS
//////////////////////////////////////////////////////////////////////

var target = Argument("target", "Default");
var configuration = Argument("configuration", "Release");
var toolpath = Argument("toolpath", @"");

//////////////////////////////////////////////////////////////////////
// PREPARATION
//////////////////////////////////////////////////////////////////////

// Define directories.
var buildDir = Directory("./Artifacts") + Directory(configuration);
GitVersion gitVersion = null; 

//////////////////////////////////////////////////////////////////////
// TASKS
//////////////////////////////////////////////////////////////////////

Task("Clean")
    .Does(() =>
{
    CleanDirectory(buildDir);
});

Task("GitVersion").Does(() => {
    gitVersion = GitVersion(new GitVersionSettings {
        UpdateAssemblyInfo = true
	});
});

Task("SyncNugetDependencies").Does(() => {
	
});

Task("Restore-NuGet-Packages")
    .IsDependentOn("Clean")
    .Does(() =>
{
    NuGetRestore("./LiquidProjections.RavenDB.sln", new NuGetRestoreSettings 
	{ 
		NoCache = true,
		Verbosity = NuGetVerbosity.Detailed,
	});
});

Task("Build")
    .IsDependentOn("Restore-NuGet-Packages")
    .Does(() =>
{
    if(IsRunningOnWindows())
    {
      // Use MSBuild
      MSBuild("./LiquidProjections.RavenDB.sln", settings => {
		settings.ToolPath = String.IsNullOrEmpty(toolpath) ? settings.ToolPath : toolpath;
		settings.ToolVersion = MSBuildToolVersion.VS2017;
        settings.PlatformTarget = PlatformTarget.MSIL;
		settings.SetConfiguration(configuration);
	  });
    }
    else
    {
      // Use XBuild
      XBuild("./LiquidProjections.RavenDB.sln", settings =>
        settings.SetConfiguration(configuration));
    }
});

Task("Run-Unit-Tests")
    .Does(() =>
{
	XUnit2("./Tests/LiquidProjections.RavenDB.Specs/**/bin/" + configuration + "/*.Specs.dll", new XUnit2Settings {
	});
});

Task("Pack")
    .IsDependentOn("GitVersion")
	.IsDependentOn("Build")
    .Does(() => 
    {
	  NuGetPack("./src/LiquidProjections.RavenDB/.nuspec", new NuGetPackSettings {
        OutputDirectory = "./Artifacts",
        Version = gitVersion.NuGetVersionV2,
		Properties = new Dictionary<string, string> {
			{ "nugetversion", gitVersion.NuGetVersionV2 }
		}
      });    
    });

//////////////////////////////////////////////////////////////////////
// TASK TARGETS
//////////////////////////////////////////////////////////////////////

Task("Default")
	.IsDependentOn("GitVersion")
    .IsDependentOn("Build")
    .IsDependentOn("Run-Unit-Tests")
    .IsDependentOn("Pack");

//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////

RunTarget(target);