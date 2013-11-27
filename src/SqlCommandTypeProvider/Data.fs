namespace FSharp.Data.SqlClient

open System
open System.Data

type internal Column = {
    Name : string
    Ordinal : int
    ClrTypeFullName : string
    IsNullable : bool
}   with
    member this.ClrType = Type.GetType this.ClrTypeFullName
    member this.ClrTypeConsideringNullable = 
        if this.IsNullable 
        then typedefof<_ option>.MakeGenericType this.ClrType 
        else this.ClrType

type internal TypeInfo = {
    SqlEngineTypeId : int
    SqlDbTypeId : int
    ClrTypeFullName : string
    UdttName : string 
    TvpColumns : Column seq
}   with
    member this.SqlDbType : SqlDbType = enum this.SqlDbTypeId
    member this.ClrType = Type.GetType this.ClrTypeFullName
    member this.TableType = this.SqlDbType = SqlDbType.Structured

type internal Parameter = {
    Name : string
    TypeInfo : TypeInfo
    Direction : ParameterDirection 
}


