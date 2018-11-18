module FSharp.Data.CreateCommandTest

open Xunit

type DB = FSharp.Data.ProgrammabilityTest.AdventureWorks

[<Fact>]
let getSingleRowNoParams() = 
    use cmd = DB.CreateCommand<"SELECT 42", SingleRow = true>()
    Assert.Equal(Some 42, cmd.Execute())    

[<Fact>]
let getSequenceWithParams() = 
    use cmd = 
        DB.CreateCommand<"
            SELECT TOP(@topN) FirstName, LastName, SalesYTD 
            FROM Sales.vSalesPerson
            WHERE CountryRegionName = @regionName AND SalesYTD > @salesMoreThan 
            ORDER BY SalesYTD
        ">(commandTimeout = 60)

    Assert.Equal(60, cmd.CommandTimeout)

    let xs = [ for x in cmd.Execute(topN = 3L, regionName = "United States", salesMoreThan = 1000000M) -> x.FirstName, x.LastName, x.SalesYTD ]

    let expected = [
        ("Pamela", "Ansman-Wolfe", 1352577.1325M)
        ("David", "Campbell", 1573012.9383M)
        ("Tete", "Mensa-Annan", 1576562.1966M)
    ]

    Assert.Equal<_ list>(expected, xs)

type MyTableType = DB.Person.``User-Defined Table Types``.MyTableType
[<Fact>]
let udttAndTuplesOutput() = 
    let cmd = DB.CreateCommand<"exec Person.myProc @x", ResultType.Tuples, SingleRow = true>()
    let p = [
        MyTableType(myId = 1, myName = Some "monkey")
        MyTableType(myId = 2, myName = Some "donkey")
    ]
    Assert.Equal(Some(1, Some "monkey"), cmd.Execute(x = p))    

[<Fact>]
let optionalParams() = 
    use cmd = DB.CreateCommand<"SELECT CAST(@x AS INT) + ISNULL(CAST(@y AS INT), 1)", SingleRow = true, AllParametersOptional = true>()
    Assert.Equal( Some( Some 14), cmd.Execute(Some 3, Some 11))    
    Assert.Equal( Some( Some 12), cmd.Execute(x = Some 11))    

[<Literal>]
let evenNumbers = "select value, svalue = cast(value as char(1)) from (values (2), (4), (6), (8)) as T(value)"

[<Fact>]
let datatableAndDataReader() = 
    use getDataReader = DB.CreateCommand<evenNumbers, ResultType.DataReader>()
    let xs = [
        use cursor = getDataReader.Execute()
        while cursor.Read() do
            yield cursor.GetInt32( 0), cursor.GetString( 1)
    ]

    let table = DB.CreateCommand<evenNumbers, ResultType.DataTable>().Execute()
    let ys = [ for row in table.Rows -> row.value, row.svalue.Value ]

    Assert.Equal<_ list>(xs, ys)

[<Fact>]
let ConditionalQuery() = 
    use cmd = DB.CreateCommand<"IF @flag = 0 SELECT 1, 'monkey' ELSE SELECT 2, 'donkey'", SingleRow=true, ResultType = ResultType.Tuples>()
    Assert.Equal(Some(1, "monkey"), cmd.Execute(flag = 0))    
    Assert.Equal(Some(2, "donkey"), cmd.Execute(flag = 1))    

[<Fact>]
let columnsShouldNotBeNull2() = 
    use cmd = DB.CreateCommand<"
        SELECT COLUMN_NAME, IS_NULLABLE, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_NAME = 'DatabaseLog' and numeric_precision is null
        ORDER BY ORDINAL_POSITION
    ", ResultType.Tuples, SingleRow = true>()

    let _,_,_,_,precision = cmd.Execute().Value
    Assert.Equal(None, precision) 

[<Fact>]
let toTraceString() =
    let now = System.DateTime.Now
    let num = 42
    let expected = sprintf "exec sp_executesql N'SELECT CAST(@Date AS DATE), CAST(@Number AS INT)',N'@Date Date,@Number Int',@Date='%A',@Number='%d'" now num
    let cmd = DB.CreateCommand<"SELECT CAST(@Date AS DATE), CAST(@Number AS INT)", ResultType.Tuples>()
    Assert.Equal<string>(
        expected, 
        actual = cmd.ToTraceString( now, num)
    )

[<Fact>]
let resultSetMapping() =
    let cmd = DB.CreateCommand<"SELECT * FROM (VALUES ('F#', 2005), ('Scala', 2003), ('foo bar',NULL))  AS T(lang, DOB)", ResultType.DataReader>()

    let readToMap(reader : System.Data.SqlClient.SqlDataReader) = 
        seq {
            try 
                while(reader.Read()) do
                    yield [| 
                        for i = 0 to reader.FieldCount - 1 do
                            if not(reader.IsDBNull(i)) then yield reader.GetName(i), reader.GetValue(i)
                    |] |> Map.ofArray
            finally
                reader.Close()
        }

    let expected = 
        [| 
            "F#", Some 2005 
            "Scala", Some 2003
            "foo bar", None
        |] 
        |> Array.map (fun(name, value) ->
            let result = Map.ofList [ "lang", box name ]
            match value with
            | Some x -> result.Add("DOB", box x)
            | None -> result
        )

    Assert.Equal<Map<string, obj>[]>(expected, cmd.Execute() |> readToMap |> Seq.toArray)

[<Fact>]
let ``Fallback to metadata retrieval through FMTONLY``() =
    use cmd = DB.CreateCommand<"exec dbo.[Init]">()
    cmd.Execute() |> ignore

[<Fact>]
let ``Runtime column names``() =
    use cmd = DB.CreateCommand<"exec dbo.[Get]", ResultType.DataReader>()
    Assert.False( cmd.Execute().NextResult())

open System.Data.SqlClient
type SqlDataReader with
    member this.ToRecords<'T>() = 
        seq {
            while this.Read() do
                let data = dict [ for i = 0 to this.VisibleFieldCount - 1 do yield this.GetName(i), this.GetValue(i)]
                yield FSharp.Data.SqlClient.DynamicRecord(data) |> box |> unbox<'T>
        }

[<Literal>]
let getDatesQuery = "SELECT GETDATE() AS Now, GETUTCDATE() AS UtcNow"

[<Fact>]
let CreareDynamicRecords() =
    use cmd = DB.CreateCommand<getDatesQuery, TypeName = "GetDatesQuery">()
    use conn = new SqlConnection(ConnectionStrings.AdventureWorksLiteral)
    conn.Open()
    let cmd = new System.Data.SqlClient.SqlCommand(getDatesQuery, conn)

    cmd.ExecuteReader().ToRecords<DB.Commands.GetDatesQuery.Record>() 
    |> Seq.toArray
    |> ignore


[<Fact>]
let ``connection is properly disposed`` () =
  
  let mutable connection : SqlConnection = null 
  let mutable isDisposed = false
  do
    use cmd = DB.CreateCommand<"exec dbo.[Get]", ResultType.DataReader>()
    connection <- (cmd :> ISqlCommand).Raw.Connection
    connection.Disposed.Add(fun _ -> isDisposed <- true)
    use reader = cmd.Execute()
    ()

  Assert.True isDisposed