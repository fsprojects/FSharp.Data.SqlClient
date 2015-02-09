module ConnectionStrings

    [<Literal>]
    let AdventureWorksLiteral = @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True"
    [<Literal>]
    let AdventureWorksLiteralMultipleActiveResults = @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True;MultipleActiveResultSets=True"
    [<Literal>]
    let AdventureWorksNamed = @"name=AdventureWorks2012"
    [<Literal>]
    let AdventureWorksAzure = @"Server=tcp:mhknbn2kdz.database.windows.net,1433;Database=AdventureWorks2012;User ID=sqlfamily;Password= sqlf@m1ly;Trusted_Connection=False;Encrypt=True;Connection Timeout=30"
    [<Literal>]
    let MasterDb = @"name=MasterDb"
    [<Literal>]
    let LocalDbDefault = @"Data Source=(LocalDb)\v11.0;Integrated Security=True"
    [<Literal>]
    let TempDb = "Data Source=.;Initial Catalog=tempdb;Integrated Security=True;"

