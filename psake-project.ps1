Include "packages\Hangfire.Build.0.3.3\tools\psake-common.ps1"

Task Default -Depends Pack

Task Test -Depends Compile -Description "Run unit and integration tests." {
    Exec { dotnet test --no-build -c release "tests\Cronos.Tests" }
}

Task Collect -Depends Test -Description "Copy all artifacts to the build folder." {
    Collect-Assembly "Cronos" "net40"
    Collect-Assembly "Cronos" "net45"
    Collect-Assembly "Cronos" "netstandard1.0"
    Collect-Assembly "Cronos" "netstandard2.0"
    Collect-File "LICENSE"
    Collect-File "README.md"
}

Task Pack -Depends Collect -Description "Create NuGet packages and archive files." {
    $version = Get-PackageVersion

    Create-Archive "Cronos-$version"
    Create-Package "Cronos" $version
}