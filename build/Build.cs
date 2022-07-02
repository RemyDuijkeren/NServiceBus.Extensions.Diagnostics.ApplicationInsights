using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.Coverlet;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Utilities.Collections;
using Nuke.Common.CI.GitHubActions;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

class Build : NukeBuild
{
    public static int Main () => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;
    
    [Parameter] readonly string NugetApiKey;
    
    [Solution] readonly Solution Solution;
    [CI] readonly GitHubActions GitHubActions;
    [GitRepository] readonly GitRepository GitRepository;

    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath TestsDirectory => RootDirectory / "tests";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
    AbsolutePath TestResultDirectory => ArtifactsDirectory / "test-results";

    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
            TestsDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
            EnsureCleanDirectory(ArtifactsDirectory);
        });

    Target Restore => _ => _
        .Executes(() =>
        {
            DotNetRestore(s => s.SetProjectFile(Solution));
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetBuild(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .EnableNoRestore());
        });
    
    Target Test => _ => _
        .DependsOn(Compile)
        .Produces(TestResultDirectory)
        .Executes(() =>
        {
            DotNetTest(s1 => s1
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .SetLoggers("trx")
                .SetResultsDirectory(TestResultDirectory)
                .EnableNoBuild()
                .EnableNoRestore()
                .When(IsServerBuild, s2 => s2.EnableUseSourceLink()));
        });

    Target Pack => _ => _
        .DependsOn(Compile)
        .After(Test)
        .Produces(ArtifactsDirectory / "*.nupkg")
        .Executes(() =>
        {
            DotNetPack(s => s
                .SetProject(Solution.GetProject("NServiceBus.Extensions.Diagnostics.ApplicationInsights"))
                .SetConfiguration(Configuration)
                .EnableIncludeSource()
                .SetOutputDirectory(ArtifactsDirectory)
                .EnableContinuousIntegrationBuild()
                .EnableNoBuild()
                .EnableNoRestore());
        });

    Target PushGitHubPackage => _ => _
        .DependsOn(Pack)
        .TriggeredBy(Pack)
        .OnlyWhenStatic(() => IsServerBuild)
        .OnlyWhenStatic(() => GitHubActions != null)
        .Executes(() =>
        {
            // GitHub doesn't allow symbols (.snupkg) yet
            DotNetNuGetPush(s => s
                .SetTargetPath(ArtifactsDirectory / "*.nupkg")
                .SetSource($"https://nuget.pkg.github.com/{GitHubActions.RepositoryOwner}/index.json") 
                .SetApiKey(GitHubActions.Token));
        });

    Target PushNuGet => _ => _
        .DependsOn(Pack)
        .OnlyWhenStatic(() => IsServerBuild)
        .OnlyWhenStatic(() => NugetApiKey != null)
        .Executes(() =>
        {
            DotNetNuGetPush(s => s
                .SetTargetPath(ArtifactsDirectory / "*.nupkg")
                .SetSource("https://api.nuget.org/v3/index.json") 
                .SetApiKey(NugetApiKey));
        });

    Target CI => _ => _
        .DependsOn(Clean, Restore, Compile, Test, Pack);
    
    Target Release => _ => _
        .DependsOn(Clean, Restore, Compile, Test, Pack, PushNuGet);
}
