(*** hide ***)
#r "../../bin/FSharp.Data.SqlClient.dll"
#r "Microsoft.SqlServer.Types"

(**
Dynamic creation of offline MDF
===============================

Sometimes you don't want to have to be online just to compile your programs, or
you might not have access to your production database from your CI systems. With
FSharp.Data.SqlClient you can use a local .MDF file as the compile-time
connection string, and then use a different connection string when you deploy
your application.

A connection string to a local .MDF file might look like this:
*)

open FSharp.Data

[<Literal>]
let compileConnectionString =
    @"Data Source=(LocalDB)\MSSQLLocalDB;AttachDbFilename=C:\git\Project1\Database1.mdf;Integrated Security=True"

(**
However, binary files like this are difficult to diff/merge when working with
multiple developers, so you might not want to check them in. Wouldn't it be nice
to store your schema in a plain text file, and have it dynamically create the
MDF file for compile time?

Well the following scripts can do that for your project.

First create a file called `createDb.ps1` and place it in an `SQL` subfolder in
your project (you can place it in the project root too, if you want):

    param(
      [Parameter(Mandatory=$true)][String]$DbName,
      [Parameter(Mandatory=$true)][String]$DbScript
    )

    $detach_db_sql = @"
    IF (SELECT COUNT(*) FROM sys.databases WHERE name = '$DbName') > 0
      EXEC sp_detach_db @dbname = N'$DbName'
    "@

    $detach_db_sql | Out-File "detachdb.sql"
    sqlcmd -S "(LocalDB)\MSSQLLocalDB" -i "detachdb.sql"
    Remove-Item "detachdb.sql"

    if (Test-Path "$PSScriptRoot\$DbName.mdf") { Remove-Item "$PSScriptRoot\$DbName.mdf" }
    if (Test-Path "$PSScriptRoot\$DbName.ldf") { Remove-Item "$PSScriptRoot\$DbName.ldf" }

    $create_db_sql = @"
    CREATE DATABASE $DbName
    ON (
      NAME = ${DbName}_dat,
      FILENAME = '$PSScriptRoot\$DbName.mdf'
    )
    LOG ON (
      NAME = ${DbName}_log,
      FILENAME = '$PSScriptRoot\$DbName.ldf'
    )
    "@

    $create_db_sql | Out-File "createdb.sql"
    sqlcmd -S "(LocalDB)\MSSQLLocalDB" -i "createdb.sql"
    Remove-Item "createdb.sql"

    sqlcmd -S "(LocalDB)\MSSQLLocalDB" -i "$DbScript"

    $detach_db_sql | Out-File "detachdb.sql"
    sqlcmd -S "(LocalDB)\MSSQLLocalDB" -i "detachdb.sql"
    Remove-Item "detachdb.sql"

Then change your connection string to look like this
*)

[<Literal>]
let compileConnectionString =
    @"Data Source=(LocalDB)\MSSQLLocalDB;AttachDbFilename=" + __SOURCE_DIRECTORY__ + @"\Database1.mdf;Integrated Security=True;Connect Timeout=10"

type Foo = SqlCommandProvider<"SELECT * FROM Foo", compileConnectionString>

let myResults = (new Foo("Use your Runtime connectionString here")).Execute()

(**
Lastly, edit your `.fsproj` file and add the following to the very end right
before `</Project>`:

    <ItemGroup>
      <SqlFiles Include="**\*.sql" />
      <BuildDbPsScript Include="SQL\createDb.ps1" />
      <BuildDbSqlScripts Include="SQL\create_myDb1.sql" DbName="Db1" />
      <BuildDbSqlScripts Include="SQL\create_myDb2.sql" DbName="Db2" />
      <UpToDateCheckInput Include="@(SqlFiles)" />
      <UpToDateCheckInput Include="@(BuildDbPsScript)" />
      <UpToDateCheckInput Include="@(BuildDbSqlScripts)" />
      <UpToDateCheckInput Include="@(BuildDbSqlScripts -> 'SQL\%(DbName).mdf')" />
      <UpToDateCheckInput Include="@(BuildDbSqlScripts -> 'SQL\%(DbName).ldf')" />
    </ItemGroup>

    <Target Name="BuildDb" BeforeTargets="BeforeBuild" Inputs="@(BuildDbSqlScripts);@(BuildDbPsScript)" Outputs="SQL\%(BuildDbSqlScripts.DbName).mdf;SQL\%(BuildDbSqlScripts.DbName).ldf">
      <Message Text="DB files missing or outdated. Building out database %(BuildDbSqlScripts.DbName) using script %(BuildDbSqlScripts.Identity)" Importance="High" />
      <Exec Command="PowerShell -NoProfile -ExecutionPolicy Bypass -Command &quot;&amp; { @(BuildDbPsScript) -DbName %(BuildDbSqlScripts.DbName) -DbScript %(BuildDbSqlScripts.Identity) }&quot;" />
    </Target>

    <Target Name="TouchProjectFileIfSqlOrDbChanged" BeforeTargets="BeforeBuild" Inputs="@(SqlFiles);@(BuildDbPsScript);@(BuildDbSqlScripts)" Outputs="$(MSBuildProjectFile)">
      <Message Text="SQL or DB files changed. Changing project file modification time to force recompilation." Importance="High" />
      <Exec Command="PowerShell -NoProfile -ExecutionPolicy Bypass -Command &quot;(dir $(MSBuildProjectFile)).LastWriteTime = Get-Date&quot;" />
    </Target>

Now when you build, it will create the databases `SQL\Db1.mdf` and `SQL\Db2.mdf`
using the scripts `SQL\create_myDb1.sql` and `SQL\create_myDb2.sql`. It will
then compile against this dynamically generated MDF file so you'll get full
static type checking without the hassle of having to have an internet
connection, or deal with binary .MDF files!

Furthermore, the `.fsproj` edits above give the following benefits:

  * The DBs are rebuilt if their corresponding SQL scripts have changed, or if the
    PowerShell script has changed
  * The project is rebuilt if the PowerShell script has changed
  * The project is rebuilt if any SQL file has changed (both the database creation
    scripts, and any other SQL scripts that SqlClient might use though the
    `SqlFile` type provider)
  * Incremental build - each database is only built if its corresponding SQL
    script or the PowerShell script has changed

When it comes to actually making the database creation scripts (such as the
`create_myDb1.sql` in the example above), you can do this if you use SQL Server
Management Studio (SSMS):

  * Connect to the database you want to copy
  * Right-click the database and select Tasks -> Generate scripts
  * Select what you need to be exported (for example, everything except Users).
  * If SqlClient throws errors when connecting to your local database, you might
    be missing important objects from your database. Make sure everything you need
    is enabled in SSMS under Tools -> Options -> SQL Server Object Explorer ->
    Scripting. For example, if you have indexed views and use the `WITH
    (NOEXPAND)` hint in your SQL, you need the indexes too, which are not enabled
    by default. In this case, enable "Script indexes" under the "Table and view
    options" heading.
*)
