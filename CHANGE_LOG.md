# Unreleased

# 2023-12

## dotnet SDK and CI Infrastructure updates

* cleanup: delete xunit test output from the repository
* adjust all tests to be under FSharp.Data.SqlClient.Tests namespace, removing all the conditional about "legacy namespace" support in context of tests.
* adjust .sln to have few more useful stuff in the solution explorer
* few launch settings to make it possible to launch in debug vscode
* appveyor, what breaks if I switch this?
* try VS2022 appveyor image
* net start mssql 2019 in appveyor
* pick msbuild from vs2022 or vs2019 and remove older ones (vs2017 and msbuild15.0)