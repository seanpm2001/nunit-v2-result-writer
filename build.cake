#tool nuget:?package=NUnit.ConsoleRunner&version=3.10.0
#tool nuget:?package=NUnit.ConsoleRunner&version=3.11.1
#tool nuget:?package=NUnit.Extension.NUnitProjectLoader&version=3.6.0
//#tool nuget:?package=NUnit.ConsoleRunner&version=3.12.0-beta1&prerelease

////////////////////////////////////////////////////////////////////
// ARGUMENTS
//////////////////////////////////////////////////////////////////////

// NOTE: These two constants are set here because constants.cake
// isn't loaded until after the arguments are parsed.
//
// NOTE: Since GitVersion is only used when running under
// Windows, the default version should be updated to the 
// next version after each release.
const string DEFAULT_VERSION = "3.7.0";
const string DEFAULT_CONFIGURATION = "Release";

var target = Argument("target", "Default");
var configuration = Argument("configuration", DEFAULT_CONFIGURATION);

#load cake/parameters.cake

//////////////////////////////////////////////////////////////////////
// SETUP AND TEARDOWN
//////////////////////////////////////////////////////////////////////

Setup<BuildParameters>((context) =>
{
	var parameters = BuildParameters.Create(context);

	Information("Building {0} version {1} of TestCentric GUI.", parameters.Configuration, parameters.PackageVersion);

	return parameters;
});

//////////////////////////////////////////////////////////////////////
// DUMP SETTINGS
//////////////////////////////////////////////////////////////////////

Task("DumpSettings")
	.Does<BuildParameters>((parameters) =>
	{
		parameters.DumpSettings();
	});

//////////////////////////////////////////////////////////////////////
// CLEAN
//////////////////////////////////////////////////////////////////////

Task("Clean")
    .Does<BuildParameters>((parameters) =>
{
    CleanDirectory(parameters.OutputDirectory);
});


//////////////////////////////////////////////////////////////////////
// INITIALIZE FOR BUILD
//////////////////////////////////////////////////////////////////////

Task("NuGetRestore")
    .Does(() =>
{
    NuGetRestore(SOLUTION_FILE, new NuGetRestoreSettings()
	{
		Source = PACKAGE_SOURCES
	});
});

//////////////////////////////////////////////////////////////////////
// BUILD
//////////////////////////////////////////////////////////////////////

Task("Build")
    .IsDependentOn("NuGetRestore")
    .Does<BuildParameters>((parameters) => 
	{
		if(IsRunningOnWindows())
		{
			MSBuild(SOLUTION_FILE, new MSBuildSettings()
				.SetConfiguration(parameters.Configuration)
				.SetMSBuildPlatform(MSBuildPlatform.Automatic)
				.SetVerbosity(Verbosity.Minimal)
				.SetNodeReuse(false)
				.SetPlatformTarget(PlatformTarget.MSIL)
			);
		}
		else
		{
			XBuild(SOLUTION_FILE, new XBuildSettings()
				.WithTarget("Build")
				.WithProperty("Configuration", parameters.Configuration)
				.SetVerbosity(Verbosity.Minimal)
			);
		}
    });

//////////////////////////////////////////////////////////////////////
// TEST
//////////////////////////////////////////////////////////////////////

Task("Test")
	.IsDependentOn("Build")
	.Does<BuildParameters>((parameters) =>
	{
		// This version is used for the unit tests
		var runner = parameters.GetPathToConsoleRunner("3.11.1");
		string unitTests = parameters.Net20OutputDirectory + UNIT_TEST_ASSEMBLY;

		int rc = StartProcess(runner, unitTests);
		if (rc == 1)
			throw new System.Exception($"{rc} test failed.");
		else if (rc > 1)
			throw new System.Exception($"{rc} tests failed.");
		else if (rc < 0)
			throw new System.Exception($"Error code {rc}.");
	});

//////////////////////////////////////////////////////////////////////
// PACKAGING
//////////////////////////////////////////////////////////////////////

Task("PackageNuGet")
	.IsDependentOn("Build")
	.Does<BuildParameters>((parameters) =>
	{
		CreateDirectory(parameters.PackageDirectory);

		BuildNuGetPackage(parameters);

		var tester = new NuGetPackageTester(parameters);

		//tester.InstallPackage();
 		CleanDirectory(parameters.NuGetInstallDirectory);
 		Unzip(parameters.NuGetPackage, parameters.NuGetInstallDirectory);
		//tester.VerifyPackage();
		Check.That(parameters.NuGetInstallDirectory,
			HasFiles("CHANGES.txt", "LICENSE.txt"),
			HasDirectory("tools/net20").WithFile("nunit-v2-result-writer.dll"),
			HasDirectory("tools/netcoreapp2.1").WithFile("nunit-v2-result-writer.dll"));

		tester.RunPackageTests();

		// In case of error, this will not be executed, leaving the directory available for examination
		//tester.UninstallPackage();
 		//DeleteDirectory(parameters.NuGetInstallDirectory, new DeleteDirectorySettings() { Recursive = true });
    });

Task("PackageChocolatey")
	.IsDependentOn("Build")
	.Does<BuildParameters>((parameters) =>
	{
		CreateDirectory(parameters.PackageDirectory);

		BUildChocolateyPackage(parameters);

		var tester = new ChocolateyPackageTester(parameters);

		//tester.InstallPackage();
 		CleanDirectory(parameters.ChocolateyInstallDirectory);
 		Unzip(parameters.ChocolateyPackage, parameters.ChocolateyInstallDirectory);
		//tester.VerifyPackage();
		Check.That(parameters.ChocolateyInstallDirectory,
			HasDirectory("tools").WithFiles("CHANGES.txt", "LICENSE.txt", "VERIFICATION.txt"),
			HasDirectory("tools/net20").WithFile("nunit-v2-result-writer.dll"),
			HasDirectory("tools/netcoreapp2.1").WithFile("nunit-v2-result-writer.dll"));

		tester.RunPackageTests();

		// In case of error, this will not be executed, leaving the directory available for examination
		//tester.UninstallPackage();
 		DeleteDirectory(parameters.ChocolateyInstallDirectory, new DeleteDirectorySettings() { Recursive = true });
	});

//////////////////////////////////////////////////////////////////////
// PUBLISH
//////////////////////////////////////////////////////////////////////

static bool hadPublishingErrors = false;

Task("PublishPackages")
	.Description("Publish nuget and chocolatey packages according to the current settings")
	.IsDependentOn("PublishToMyGet")
	// .IsDependentOn("PublishToNuGet")
	// .IsDependentOn("PublishToChocolatey")
	.Does(() =>
	{
		if (hadPublishingErrors)
			throw new Exception("One of the publishing steps failed.");
	});

// This task may either be run by the PublishPackages task,
// which depends on it, or directly when recovering from errors.
Task("PublishToMyGet")
	.Description("Publish packages to MyGet")
	.Does<BuildParameters>((parameters) =>
	{
		if (!parameters.ShouldPublishToMyGet)
			Information("Nothing to publish to MyGet from this run.");
		else
			try
			{
				PushNuGetPackage(parameters.NuGetPackage, parameters.MyGetApiKey, parameters.MyGetPushUrl);
				PushChocolateyPackage(parameters.ChocolateyPackage, parameters.MyGetApiKey, parameters.MyGetPushUrl);
			}
			catch (Exception)
			{
				hadPublishingErrors = true;
			}
	});

//////////////////////////////////////////////////////////////////////
// TASK TARGETS
//////////////////////////////////////////////////////////////////////

Task("Package")
	.IsDependentOn("PackageNuGet")
	.IsDependentOn("PackageChocolatey");

Task("Full")
	.IsDependentOn("Clean")
	.IsDependentOn("Build")
	.IsDependentOn("Test")
	.IsDependentOn("Package");

Task("Appveyor")
	.IsDependentOn("Build")
	.IsDependentOn("Test")
	.IsDependentOn("Package")
	.IsDependentOn("PublishPackages");

Task("Travis")
	.IsDependentOn("Build")
	.IsDependentOn("Test");

Task("Default")
    .IsDependentOn("Build");

//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////

RunTarget(target);
