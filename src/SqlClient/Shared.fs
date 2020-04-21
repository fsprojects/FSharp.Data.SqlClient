namespace FSharp.Data

open System
open System.Data
open System.Collections.Generic
open System.Data.SqlClient

///<summary>Enum describing output type</summary>
type ResultType =
///<summary>Sequence of custom records with properties matching column names and types</summary>
    | Records = 0
///<summary>Sequence of tuples matching column types with the same order</summary>
    | Tuples = 1
///<summary>Typed DataTable <see cref='T:FSharp.Data.DataTable`1'/></summary>
    | DataTable = 2
///<summary>raw DataReader</summary>
    | DataReader = 3

type SqlEnumKind = 
| Default = 0
| CLI = 1
| UnitsOfMeasure = 2

[<CompilerMessageAttribute("This API supports the FSharp.Data.SqlClient infrastructure and is not intended to be used directly from your code.", 101, IsHidden = true)>]
[<RequireQualifiedAccess>]
type ResultRank = 
    | Sequence = 0
    | SingleRow = 1
    | ScalarValue = 2

type Mapper private() = 
    static member GetMapperWithNullsToOptions(nullsToOptions, mapper: obj[] -> obj) = 
        fun values -> 
            nullsToOptions values
            mapper values
            
    static member SetRef<'t>(r : byref<'t>, arr: (string * obj)[], i) = 
        let value = arr.[i] |> snd
        r <-
            match value with
            | :? 't as v -> v
            | _ (* dbnull *) -> Unchecked.defaultof<'t>

type Column = {
    Name: string
    TypeInfo: TypeInfo
    Nullable: bool
    MaxLength: int
    ReadOnly: bool
    Identity: bool
    PartOfUniqueKey: bool
    DefaultConstraint: string
    Description: string
    Precision: byte
    Scale: byte
}   with
    member this.ErasedToType = 
        if this.Nullable
        then typedefof<_ option>.MakeGenericType this.TypeInfo.ClrType
        else this.TypeInfo.ClrType
    
    member this.GetProvidedType(?unitsOfMeasurePerSchema: Dictionary<string, ProviderImplementation.ProvidedTypes.ProvidedTypeDefinition list>) = 
        let typeConsideringUOM: Type = 
            if this.TypeInfo.IsUnitOfMeasure && unitsOfMeasurePerSchema.IsSome
            then
                assert(unitsOfMeasurePerSchema.IsSome)
                let uomType = unitsOfMeasurePerSchema.Value.[this.TypeInfo.Schema] |> List.find (fun x -> x.Name = this.TypeInfo.UnitOfMeasureName)
                ProviderImplementation.ProvidedTypes.ProvidedMeasureBuilder.AnnotateType(this.TypeInfo.ClrType, [ uomType ])
            else
                this.TypeInfo.ClrType

        if this.Nullable
        then
            //ProviderImplementation.ProvidedTypes.ProvidedTypeBuilder.MakeGenericType(typedefof<_ option>, [ typeConsideringUOM ])
            typedefof<_ option>.MakeGenericType typeConsideringUOM
        else 
            typeConsideringUOM

    member this.HasDefaultConstraint = this.DefaultConstraint <> ""
    member this.NullableParameter = this.Nullable || this.HasDefaultConstraint

    static member Parse(cursor: SqlDataReader, typeLookup: int * int option -> TypeInfo, ?defaultValue, ?description) = {
        Name = unbox cursor.["name"]
        TypeInfo = 
            let system_type_id = unbox<byte> cursor.["system_type_id"] |> int
            let user_type_id = cursor.TryGetValue "user_type_id"
            typeLookup(system_type_id, user_type_id)
        Nullable = unbox cursor.["is_nullable"]
        MaxLength = cursor.["max_length"] |> unbox<int16> |> int
        ReadOnly = not( cursor.GetValueOrDefault("is_updateable", false))
        Identity = cursor.GetValueOrDefault( "is_identity_column", false)
        PartOfUniqueKey = unbox cursor.["is_part_of_unique_key"]
        DefaultConstraint = defaultArg defaultValue ""
        Description = defaultArg description ""
        Precision = unbox cursor.["precision"]
        Scale = unbox cursor.["scale"]
    }

    override this.ToString() = 
        sprintf "%s\t%s\t%b\t%i\t%b\t%b\t%b\t%s\t%s" 
            this.Name 
            this.TypeInfo.ClrTypeFullName 
            this.Nullable 
            this.MaxLength
            this.ReadOnly
            this.Identity
            this.PartOfUniqueKey
            this.DefaultConstraint
            this.Description

and TypeInfo = {
    TypeName: string
    Schema: string
    SqlEngineTypeId: int
    UserTypeId: int
    SqlDbType: SqlDbType
    IsFixedLength: bool 
    ClrTypeFullName: string
    UdttName: string 
    TableTypeColumns: Column[] Lazy
}   with
    member this.ClrType: Type = Type.GetType( this.ClrTypeFullName, throwOnError = true)
    member this.TableType = this.SqlDbType = SqlDbType.Structured
    member this.IsValueType = not this.TableType && this.ClrType.IsValueType
    member this.IsUnitOfMeasure = this.TypeName.StartsWith("<") && this.TypeName.EndsWith(">")
    member this.UnitOfMeasureName = this.TypeName.TrimStart('<').TrimEnd('>')

type Parameter = {
    Name: string
    TypeInfo: TypeInfo
    Direction: ParameterDirection 
    MaxLength: int
    Precision: byte
    Scale : byte
    DefaultValue: obj option
    Optional: bool
    Description: string
}   with
    
    member this.Size = 
        match this.TypeInfo.SqlDbType with
        | SqlDbType.NChar | SqlDbType.NText | SqlDbType.NVarChar -> this.MaxLength / 2
        | _ -> this.MaxLength

    member this.GetProvidedType(?unitsOfMeasurePerSchema: Dictionary<string, ProviderImplementation.ProvidedTypes.ProvidedTypeDefinition list>) = 
        if this.TypeInfo.IsUnitOfMeasure && unitsOfMeasurePerSchema.IsSome
        then
            assert(unitsOfMeasurePerSchema.IsSome)
            let uomType = unitsOfMeasurePerSchema.Value.[this.TypeInfo.Schema] |> List.find (fun x -> x.Name = this.TypeInfo.UnitOfMeasureName)
            ProviderImplementation.ProvidedTypes.ProvidedMeasureBuilder.AnnotateType(this.TypeInfo.ClrType, [ uomType ])
        else
            this.TypeInfo.ClrType

type TempTableLoader(fieldCount, items: obj seq) =
    let enumerator = items.GetEnumerator()

    interface IDataReader with
        member this.FieldCount: int = fieldCount
        member this.Read(): bool = enumerator.MoveNext()
        member this.GetValue(i: int): obj =
            let row : obj[] = unbox enumerator.Current
            row.[i]
        member this.Dispose(): unit = ()

        member __.Close(): unit = invalidOp "NotImplementedException"
        member __.Depth: int = invalidOp "NotImplementedException"
        member __.GetBoolean(_: int): bool = invalidOp "NotImplementedException"
        member __.GetByte(_ : int): byte = invalidOp "NotImplementedException"
        member __.GetBytes(_ : int, _ : int64, _ : byte [], _ : int, _ : int): int64 = invalidOp "NotImplementedException"
        member __.GetChar(_ : int): char = invalidOp "NotImplementedException"
        member __.GetChars(_ : int, _ : int64, _ : char [], _ : int, _ : int): int64 = invalidOp "NotImplementedException"
        member __.GetData(_ : int): IDataReader = invalidOp "NotImplementedException"
        member __.GetDataTypeName(_ : int): string = invalidOp "NotImplementedException"
        member __.GetDateTime(_ : int): System.DateTime = invalidOp "NotImplementedException"
        member __.GetDecimal(_ : int): decimal = invalidOp "NotImplementedException"
        member __.GetDouble(_ : int): float = invalidOp "NotImplementedException"
        member __.GetFieldType(_ : int): System.Type = invalidOp "NotImplementedException"
        member __.GetFloat(_ : int): float32 = invalidOp "NotImplementedException"
        member __.GetGuid(_ : int): System.Guid = invalidOp "NotImplementedException"
        member __.GetInt16(_ : int): int16 = invalidOp "NotImplementedException"
        member __.GetInt32(_ : int): int = invalidOp "NotImplementedException"
        member __.GetInt64(_ : int): int64 = invalidOp "NotImplementedException"
        member __.GetName(_ : int): string = invalidOp "NotImplementedException"
        member __.GetOrdinal(_ : string): int = invalidOp "NotImplementedException"
        member __.GetSchemaTable(): DataTable = invalidOp "NotImplementedException"
        member __.GetString(_ : int): string = invalidOp "NotImplementedException"
        member __.GetValues(_ : obj []): int = invalidOp "NotImplementedException"
        member __.IsClosed: bool = invalidOp "NotImplementedException"
        member __.IsDBNull(_ : int): bool = invalidOp "NotImplementedException"
        member __.Item with get (_ : int): obj = invalidOp "NotImplementedException"
        member __.Item with get (_ : string): obj = invalidOp "NotImplementedException"
        member __.NextResult(): bool = invalidOp "NotImplementedException"
        member __.RecordsAffected: int = invalidOp "NotImplementedException"

module RuntimeInternals =
    let setupTableFromSerializedColumns (serializedSchema: string) (table: System.Data.DataTable) =
        let primaryKey = ResizeArray()
        for line in serializedSchema.Split('\n') do
            let xs = line.Split('\t')
            let col = new DataColumn()
            col.ColumnName <- xs.[0]
            col.DataType <- Type.GetType( xs.[1], throwOnError = true)  
            col.AllowDBNull <- Boolean.Parse xs.[2]
            if col.DataType = typeof<string>
            then 
                col.MaxLength <- int xs.[3]
            col.ReadOnly <- Boolean.Parse xs.[4]
            col.AutoIncrement <- Boolean.Parse xs.[5]
            if Boolean.Parse xs.[6]
            then    
                primaryKey.Add col 
            table.Columns.Add col

        table.PrimaryKey <- Array.ofSeq primaryKey

[<AutoOpen>]
module Shared =    
    let DbNull = box DBNull.Value
