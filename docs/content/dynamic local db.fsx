(*** hide ***)
#r "../../bin/FSharp.Data.SqlClient.dll"
#r "Microsoft.SqlServer.Types"

(**
Dynamic creation of offline MDF
===============================

Sometimes you don't want to have to be online just to compile your programs. With FSharp.Data.SqlClient you can use a local
.MDF file as the compile time connection string, and then change your connection string at runtime when you deploy your application.
*)

open FSharp.Data

[<Literal>]
let connectionString = @"Data Source=(LocalDB)\v12.0;AttachDbFilename=C:\git\Project1\Database1.mdf;Integrated Security=True;Connect Timeout=10"

(**
However, binary files like this are difficult to diff/merge when working with multiple developers. For this reason wouldn't it be nice
to store your schema in a plain text file, and have it dynamically create the MDF file for compile time?

Well the following scripts can do that for your project.

First create a file called `createdb.ps1`:

    # this is the name that Fsharp.Data.SqlClient TypeProvider expects it to be at build time
    $new_db_name = "Database1" 

    $detach_db_sql = @"
    use master;
    GO
    EXEC sp_detach_db @dbname = N'$new_db_name';
    GO
    "@

    $detach_db_sql | Out-File "detachdb.sql"
    sqlcmd -S "(localdb)\v11.0" -i detachdb.sql
    Remove-Item .\detachdb.sql

    Remove-Item "$new_db_name.mdf"
    Remove-Item "$new_db_name.ldf"

    $create_db_sql = @"
        USE master ;
        GO
        CREATE DATABASE $new_db_name
        ON 
        ( NAME = Sales_dat,
            FILENAME = '$PSScriptRoot\$new_db_name.mdf',
            SIZE = 10,
            MAXSIZE = 50,
            FILEGROWTH = 5 )
        LOG ON
        ( NAME = Sales_log,
            FILENAME = '$PSScriptRoot\$new_db_name.ldf',
            SIZE = 5MB,
            MAXSIZE = 25MB,
            FILEGROWTH = 5MB ) ;
        GO
    "@

    $create_db_sql | Out-File "createdb.sql"
    sqlcmd -S "(localdb)\v11.0" -i createdb.sql
    Remove-Item .\createdb.sql

    sqlcmd -S "(localdb)\v11.0" -i schema.sql

    $detach_db_sql | Out-File "detachdb.sql"
    sqlcmd -S "(localdb)\v11.0" -i detachdb.sql
    Remove-Item .\detachdb.sql

Then change your connection string to look like this
*)

[<Literal>]
let connectionStringForCompileTime = @"Data Source=(LocalDB)\v12.0;AttachDbFilename=" + __SOURCE_DIRECTORY__ + @"\Database1.mdf;Integrated Security=True;Connect Timeout=10"

type Foo = SqlCommandProvider<"SELECT * FROM Foo", connectionStringForCompileTime>

let myResults = (new Foo("Use your Runtime connectionString here")).Execute()

(**
Lastly, edit your `.fsproj` file and add the following to the very end right before `</Project>`

    <Target Name="BeforeBuild">
        <Message Text="Building out SQL Database: Database1.mdf" Importance="High" />
        <Exec Command="PowerShell -NoProfile -ExecutionPolicy Bypass -Command &quot;&amp; { $(ProjectDir)Createdb.ps1 }&quot;" />
    </Target>

Now when you build, it will create a database named `Database1` and then look for a file called `schema.sql` which will be used
to create the database.  It will then compile against this dynamically generated MDF file so you'll get full static type checking
without the hassle of having to have an internet connection, or deal with binary .MDF files!

*)