#if WITH_LEGACY_NAMESPACE
module FSharp.Data.SpReturnValueTests
open FSharp.Data.SqlClient
#else
module FSharp.Data.SqlClient.SpReturnValueTests
#endif

open System
open Xunit

    type AdventureWorks = SqlProgrammabilityProvider<ConnectionStrings.AdventureWorksNamed, UseReturnValue = true>

    [<Fact>]
    let AddRef() = 
        let cmd = new AdventureWorks.dbo.AddRef()
        let x, y = 12, -1
        let sum = ref Int32.MinValue
        let returnValue = ref Int32.MaxValue
        let rowsAffected = cmd.Execute(x, y, sum, returnValue) 
        Assert.Equal(-1, rowsAffected) 
        Assert.Equal(x + y, !sum)
        Assert.Equal(!sum, !returnValue)
        //tupled syntax
        let rowAffected2, sum2, returnValue2 = cmd.Execute(x, y)
        Assert.Equal(x + y, sum2)
        Assert.Equal(sum2, returnValue2)
        Assert.Equal(-1, rowAffected2) 

    type DboMyTableType = AdventureWorks.dbo.``User-Defined Table Types``.MyTableType 

    [<Fact>]
    let ResultSetAndOutParam() = 
        let cmd = new AdventureWorks.dbo.HowManyRows()
        let p = [
            DboMyTableType(myId = 1)
            DboMyTableType(myId = 2, myName = Some "donkey")
        ]

        do //explicit refs
            let total = ref Int64.MinValue
            let returnValue = ref Int32.MaxValue
            let result = cmd.Execute(p, total, returnValue) 
            Assert.Equal<_ list>( [ 2, Some "donkey" ], [ for x in result -> x.myId, x.myName ] )
            Assert.Equal(2L, !total)
            Assert.Equal(0, !returnValue) //default return value

        do //tupled response syntax
            let result, total, returnValue = cmd.Execute(p) 
            Assert.Equal<_ list>( [ 2, Some "donkey" ], [ for x in result -> x.myId, x.myName ] )
            Assert.Equal(2L, total)
            Assert.Equal(0, returnValue) //default return value

    [<Fact>]
    let BinaryOutput() =
        let cmd = new AdventureWorks.dbo.BinaryOutput()
        let _, out, returnValue = cmd.Execute()
        Assert.Equal<byte []>([| 0uy; 0uy; 0uy; 0uy; 42uy |], out)
        Assert.Equal(0, returnValue)

    [<Fact>]
    let TimestampOutput() =
        let cmd = new AdventureWorks.dbo.TimestampOutput()
        let _, timestamp, returnValue = cmd.Execute()
        Assert.Equal<byte []>([| 0uy; 0uy; 0uy; 0uy; 0uy; 0uy; 0uy; 42uy; |], timestamp)
        Assert.Equal(0, returnValue)
