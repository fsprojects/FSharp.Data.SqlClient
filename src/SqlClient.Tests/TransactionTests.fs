module FSharp.Data.TransactionTests

open System.Data.SqlClient
open System.Transactions

open Xunit

open FSharp.Data.TypeProviderTest

[<Literal>]
let connectionString = @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True"


[<Fact>]
let implicit() =
    (new DeleteBitCoin()).Execute(bitCoinCode) |> ignore
    begin
        use tran = new TransactionScope() 
        Assert.Equal(1, (new InsertBitCoin()).Execute(bitCoinCode, bitCoinName))
        Assert.Equal(1, (new GetBitCoin()).Execute(bitCoinCode) |> Seq.length)
    end
    Assert.Equal(0, (new GetBitCoin()).Execute(bitCoinCode) |> Seq.length)

[<Fact>]
let implicitAsync() =
    (new DeleteBitCoin()).Execute(bitCoinCode) |> ignore
    begin
        use tran = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) 
        Assert.Equal(1, (new InsertBitCoin()).AsyncExecute(bitCoinCode, bitCoinName) |> Async.RunSynchronously)
        Assert.Equal(1, (new GetBitCoin()).AsyncExecute(bitCoinCode) |> Async.RunSynchronously |> Seq.length)
    end
    Assert.Equal(0, (new GetBitCoin()).Execute(bitCoinCode) |> Seq.length)

[<Fact>]
let implicitAsyncNETBefore451() =
    (new DeleteBitCoin()).Execute(bitCoinCode) |> ignore
    begin
        use tran = new TransactionScope() 
        use conn = new SqlConnection(connectionString)
        conn.Open()
        conn.EnlistTransaction(Transaction.Current)
        let localTran = conn.BeginTransaction()
        Assert.Equal(1, (new InsertBitCoin(localTran)).AsyncExecute(bitCoinCode, bitCoinName) |> Async.RunSynchronously)
        Assert.Equal(1, (new GetBitCoin(localTran)).AsyncExecute(bitCoinCode) |> Async.RunSynchronously |> Seq.length)
    end
    Assert.Equal(0, (new GetBitCoin()).Execute(bitCoinCode) |> Seq.length)

[<Fact>]
let local() =
    (new DeleteBitCoin()).Execute(bitCoinCode) |> ignore
    use conn = new SqlConnection(connectionString)
    conn.Open()
    let tran = conn.BeginTransaction()
    Assert.Equal(1, (new InsertBitCoin(tran)).Execute(bitCoinCode, bitCoinName))
    Assert.Equal(1, (new GetBitCoin(tran)).Execute(bitCoinCode) |> Seq.length)
    tran.Rollback()
    Assert.Equal(0, (new GetBitCoin()).Execute(bitCoinCode) |> Seq.length)
