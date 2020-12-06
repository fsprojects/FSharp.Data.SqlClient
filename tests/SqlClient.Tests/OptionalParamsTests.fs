#if WITH_LEGACY_NAMESPACE
module FSharp.Data.OptionalParamsTests
open FSharp.Data.SqlClient
#else
module FSharp.Data.SqlClient.OptionalParamsTests
#endif


open Xunit
[<Literal>]
let connection = ConnectionStrings.AdventureWorksNamed

type QueryWithNullableParam = 
    SqlCommandProvider<"SELECT CAST(@x AS INT) + ISNULL(CAST(@y AS INT), 1)", connection, SingleRow = true, AllParametersOptional = true>

[<Fact>]
let BothOptinalParamsSupplied() = 
    use cmd = new QueryWithNullableParam()
    Assert.Equal( Some( Some 14), cmd.Execute(Some 3, Some 11))    

[<Fact>]
let SkipYParam() = 
    use cmd = new QueryWithNullableParam()
    Assert.Equal( Some( Some 12), cmd.Execute(x = Some 11))    

[<Fact>]
let NullableStringInputParameter() = 
    use cmd = new SqlCommandProvider<"select ISNULL(CAST(@P1 AS VARCHAR), '')", connection, SingleRow = true, AllParametersOptional = true>()

    Assert.Equal(
        expected = Some "",
        actual = cmd.Execute( None)
    )
    
    Assert.Equal(
        expected = Some "",
        actual = cmd.Execute()
    )

    Assert.Equal(
        expected = Some "boo",
        actual = cmd.Execute( Some "boo")
    )

[<Fact>]
let NullableStringInputMandatoryParameter() = 
    use cmd = new SqlCommandProvider<"select ISNULL(CAST(@P1 AS VARCHAR), '')", connection, SingleRow = true>()
    Assert.Equal(
        expected = Some "",
        actual = cmd.Execute( null)
    )
