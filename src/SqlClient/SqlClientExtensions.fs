[<AutoOpen>]
module FSharp.Data.SqlClient.Extensions

open System
open System.Text
open System.Data
open System.Collections.Generic
open System.IO
open System.Threading.Tasks
open System.Data.SqlClient
open System.Reflection

type Type with
    member this.PartialAssemblyQualifiedName = 
        Assembly.CreateQualifiedName(this.Assembly.GetName().Name, this.FullName)

type SqlCommand with
    member this.AsyncExecuteReader behavior =
        Async.FromBeginEnd((fun(callback, state) -> this.BeginExecuteReader(callback, state, behavior)), this.EndExecuteReader)

    member this.AsyncExecuteNonQuery() =
        Async.FromBeginEnd(this.BeginExecuteNonQuery, this.EndExecuteNonQuery) 

    static member internal DefaultTimeout = (new SqlCommand()).CommandTimeout

    member internal this.ExecuteQuery mapper = 
        seq {
            use cursor = this.ExecuteReader()
            while cursor.Read() do
                yield mapper cursor
        }

type SqlDataReader with
    member internal this.TryGetValue(name: string) = 
        let value = this.[name] 
        if Convert.IsDBNull value then None else Some(unbox<'a> value)
    member internal this.GetValueOrDefault<'a>(name: string, defaultValue) = 
        let value = this.[name] 
        if Convert.IsDBNull value then defaultValue else unbox<'a> value

let DbNull = box DBNull.Value

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
}   with
    
    member this.ClrTypeConsideringNullable = 
        if this.Nullable
        then typedefof<_ option>.MakeGenericType this.TypeInfo.ClrType
        else this.TypeInfo.ClrType

    member this.HasDefaultConstraint = this.DefaultConstraint <> ""
    member this.NullableParameter = this.Nullable || this.HasDefaultConstraint

    static member Parse(cursor: SqlDataReader, typeLookup: int * int option -> TypeInfo) = {
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
        DefaultConstraint = ""
        Description = ""
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
    SqlDbTypeId: int
    IsFixedLength: bool option
    ClrTypeFullName: string
    UdttName: string 
    TableTypeColumns: Column[] Lazy
}   with
    member this.SqlDbType : SqlDbType = enum this.SqlDbTypeId
    member this.ClrType : Type = Type.GetType( this.ClrTypeFullName, throwOnError = true)
    member this.TableType = this.SqlDbType = SqlDbType.Structured
    member this.IsValueType = not this.TableType && this.ClrType.IsValueType

type Parameter = {
    Name: string
    TypeInfo: TypeInfo
    Direction: ParameterDirection 
    DefaultValue: obj option
    Optional: bool
    Description: string
}

let private dataTypeMappings = Dictionary<string, TypeInfo[]>()

let internal clearDataTypesMap() = dataTypeMappings.Clear()

let internal getTypes( connectionString: string) = dataTypeMappings.[connectionString]

let internal findTypeInfoBySqlEngineTypeId (connStr, system_type_id, user_type_id : int option) = 
    assert (dataTypeMappings.ContainsKey connStr)

    dataTypeMappings.[connStr] 
    |> Array.filter(fun x -> 
        let result = 
            x.SqlEngineTypeId = system_type_id &&
            (user_type_id.IsSome && x.UserTypeId = user_type_id.Value || user_type_id.IsNone && x.UserTypeId = system_type_id)
        result
    ) 
    |> Seq.exactlyOne

let internal findTypeInfoByProviderType( connStr, sqlDbType)  = 
    assert (dataTypeMappings.ContainsKey connStr)
    dataTypeMappings.[connStr] |> Array.find (fun x -> x.SqlDbType = sqlDbType)

type LiteralType = Microsoft.SqlServer.TransactSql.ScriptDom.LiteralType
type UnaryExpression = Microsoft.SqlServer.TransactSql.ScriptDom.UnaryExpression

let rec parseDefaultValue (definition: string) (expr: Microsoft.SqlServer.TransactSql.ScriptDom.ScalarExpression) = 
    match expr with
    | :? Microsoft.SqlServer.TransactSql.ScriptDom.Literal as x ->
        match x.LiteralType with
        | LiteralType.Default | LiteralType.Null -> Some null
        | LiteralType.Integer -> x.Value |> int |> box |> Some
        | LiteralType.Money | LiteralType.Numeric -> x.Value |> decimal |> box |> Some
        | LiteralType.Real -> x.Value |> float |> box |> Some 
        | LiteralType.String -> x.Value |> string |> box |> Some 
        | _ -> None
    | :? UnaryExpression as x when x.UnaryExpressionType <> Microsoft.SqlServer.TransactSql.ScriptDom.UnaryExpressionType.BitwiseNot ->
        let fragment = definition.Substring( x.StartOffset, x.FragmentLength)
        match x.Expression with
        | :? Microsoft.SqlServer.TransactSql.ScriptDom.Literal as x ->
            match x.LiteralType with
            | LiteralType.Integer -> fragment |> int |> box |> Some
            | LiteralType.Money | LiteralType.Numeric -> fragment |> decimal |> box |> Some
            | LiteralType.Real -> fragment |> float |> box |> Some 
            | _ -> None
        | _  -> None 
    | _ -> None 

type Routine = 
    | StoredProcedure of schema: string * name: string * definition: string * description: string option
    | TableValuedFunction of schema: string * name: string * definition: string * description: string option
    | ScalarValuedFunction of schema: string * name: string * definition: string * description: string option

    member this.Definition = 
        match this with
        | StoredProcedure(_, _, definition, _) 
        | TableValuedFunction(_, _, definition, _) 
        | ScalarValuedFunction(_, _, definition, _) -> definition

    member this.Description = 
        match this with
        | StoredProcedure(_, _, _, description) 
        | TableValuedFunction(_, _, _, description) 
        | ScalarValuedFunction(_, _, _, description) -> description

    member this.TwoPartName = 
        match this with
        | StoredProcedure(schema, name, _, _) 
        | TableValuedFunction(schema, name, _, _) 
        | ScalarValuedFunction(schema, name, _, _) -> schema, name

    member this.IsStoredProc = match this with StoredProcedure _ -> true | _ -> false
    
    member this.ToCommantText(parameters: Parameter list) = 
        let twoPartNameIdentifier = sprintf "%s.%s" <|| this.TwoPartName
        match this with 
        | StoredProcedure _-> twoPartNameIdentifier
        | TableValuedFunction _ -> 
            parameters |> List.map (fun p -> p.Name) |> String.concat ", " |> sprintf "SELECT * FROM %s(%s)" twoPartNameIdentifier
        | ScalarValuedFunction _ ->     
            parameters |> List.map (fun p -> p.Name) |> String.concat ", " |> sprintf "SELECT %s(%s)" twoPartNameIdentifier

type SqlConnection with

 //address an issue when regular Dispose on SqlConnection needed for async computation 
 //wipes out all properties like ConnectionString in addition to closing connection to db
    member this.UseLocally(?privateConnection) =
        if this.State = ConnectionState.Closed 
            && defaultArg privateConnection true
        then 
            this.Open()
            { new IDisposable with member __.Dispose() = this.Close() }
        else { new IDisposable with member __.Dispose() = () }
    
    member internal this.CheckVersion() = 
        assert (this.State = ConnectionState.Open)
        let majorVersion = this.ServerVersion.Split('.').[0]
        if int majorVersion < 11 
        then failwithf "Minimal supported major version is 11 (SQL Server 2012 and higher or Azure SQL Database). Currently used: %s" this.ServerVersion

    member this.IsSqlAzure = 
        assert (this.State = ConnectionState.Open)
        use cmd = new SqlCommand("SELECT SERVERPROPERTY('edition')", this)
        cmd.ExecuteScalar().Equals("SQL Azure")

    member internal this.GetUserSchemas() = 
        use __ = this.UseLocally()
        use cmd = new SqlCommand("SELECT name FROM sys.schemas WHERE principal_id = 1", this)
        cmd.ExecuteQuery(fun record -> record.GetString(0)) |> Seq.toList

    member internal this.GetRoutines( schema, isSqlAzure) = 
        assert (this.State = ConnectionState.Open)

        let descriptionSelector = 
            if isSqlAzure 
            then 
                "(SELECT NULL AS Value)"
            else 
                "fn_listextendedproperty ('MS_Description', 'schema', SPECIFIC_SCHEMA, ROUTINE_TYPE, SPECIFIC_NAME, default, default)" 

        let getRoutinesQuery = sprintf "
            SELECT 
                SPECIFIC_SCHEMA
                ,SPECIFIC_NAME
                ,DATA_TYPE
                ,DEFINITION = ISNULL( OBJECT_DEFINITION( OBJECT_ID( SPECIFIC_SCHEMA + '.' + SPECIFIC_NAME)), '')  
	            ,DESCRIPTION = XProp.Value
            FROM 
                INFORMATION_SCHEMA.ROUTINES 
                OUTER APPLY %s AS XProp
            WHERE 
                ROUTINE_SCHEMA = '%s'" descriptionSelector schema

        use cmd = new SqlCommand(getRoutinesQuery, this)
        cmd.ExecuteQuery(fun x ->
            let schema, name = unbox x.["SPECIFIC_SCHEMA"], unbox x.["SPECIFIC_NAME"]
            let definition = unbox x.["DEFINITION"]
            let description = x.TryGetValue( "DESCRIPTION")
            match x.["DATA_TYPE"] with
            | :? string as x when x = "TABLE" -> TableValuedFunction(schema, name, definition, description)
            | :? DBNull -> StoredProcedure(schema, name, definition, description)
            | _ -> ScalarValuedFunction(schema, name, definition, description)
        ) 
        |> Seq.toArray
            
    member internal this.GetParameters( routine: Routine, isSqlAzure) =      
        assert (this.State = ConnectionState.Open)

        let paramDefaults = Task.Factory.StartNew( fun() ->

            let parser = Microsoft.SqlServer.TransactSql.ScriptDom.TSql120Parser( true)
            let tsqlReader = new StringReader(routine.Definition)
            let errors = ref Unchecked.defaultof<_>
            let fragment = parser.Parse(tsqlReader, errors)

            let result = Dictionary()

            fragment.Accept {
                new Microsoft.SqlServer.TransactSql.ScriptDom.TSqlFragmentVisitor() with
                    member __.Visit(node : Microsoft.SqlServer.TransactSql.ScriptDom.ProcedureParameter) = 
                        base.Visit node
                        result.[node.VariableName.Value] <- parseDefaultValue routine.Definition node.Value
            }

            result
        )

        let descriptionSelector = 
            if isSqlAzure 
            then 
                "(SELECT NULL AS Value)"
            else 
                let routineType = if routine.IsStoredProc then "PROCEDURE" else "FUNCTION"
                sprintf "fn_listextendedproperty ('MS_Description', 'schema', OBJECT_SCHEMA_NAME(object_id), '%s', OBJECT_NAME(object_id), 'PARAMETER', p.name)" routineType 

        let query = sprintf "
            SELECT
	            p.name
	            ,system_type_id AS suggested_system_type_id
	            ,user_type_id AS suggested_user_type_id
	            ,is_output AS suggested_is_output
	            ,CAST( IIF(is_output = 1, 0, 1) AS BIT) AS suggested_is_input
	            ,description = ISNULL( XProp.Value, '')
            FROM sys.all_parameters AS p
                OUTER APPLY %s AS XProp
            WHERE
                p.Name <> '' 
                AND OBJECT_ID('%s.%s') = object_id" descriptionSelector <|| routine.TwoPartName


        use cmd = new SqlCommand( query, this)
        cmd.ExecuteQuery(fun record -> 
            let name = string record.["name"]
            let direction = 
                if unbox record.["suggested_is_output"]
                then 
                    invalidArg name "Output parameters are not supported"
                else 
                    assert(unbox record.["suggested_is_input"])
                    ParameterDirection.Input 

            let system_type_id: int = unbox<byte> record.["suggested_system_type_id"] |> int
            let user_type_id = record.TryGetValue "suggested_user_type_id"

            let typeInfo = findTypeInfoBySqlEngineTypeId(this.ConnectionString, system_type_id, user_type_id)
            let defaultValue = match paramDefaults.Result.TryGetValue(name) with | true, value -> value | false, _ -> None
            let valueTypeWithNullDefault = typeInfo.IsValueType && defaultValue = Some(null)

            { 
                Name = name
                TypeInfo = typeInfo
                Direction = direction
                DefaultValue = defaultValue
                Optional = valueTypeWithNullDefault 
                Description = string record.["description"]
            }
        )
        |> Seq.toList

    member internal this.GetTables( schema, isSqlAzure) = 
        assert (this.State = ConnectionState.Open)
        let descriptionSelector = 
            if isSqlAzure 
            then 
                "(SELECT NULL AS Value)"
            else 
                "fn_listextendedproperty ('MS_Description', 'schema', TABLE_SCHEMA, 'TABLE', TABLE_NAME, default, default)" 

        let getTablesQuery = sprintf "
            SELECT 
                TABLE_NAME 
	            ,DESCRIPTION = XProp.Value
            FROM 
                INFORMATION_SCHEMA.TABLES 
                OUTER APPLY %s AS XProp
            WHERE 
                TABLE_TYPE = 'BASE TABLE' AND TABLE_SCHEMA = '%s'" descriptionSelector schema
        use cmd = new SqlCommand(getTablesQuery, this)
        cmd.ExecuteQuery(fun x -> x.GetString 0, x.TryGetValue( "DESCRIPTION") ) |> Seq.toList

    member internal this.GetFullQualityColumnInfo commandText = 
        assert (this.State = ConnectionState.Open)
        use cmd = new SqlCommand("sys.sp_describe_first_result_set", this, CommandType = CommandType.StoredProcedure)
        cmd.Parameters.AddWithValue("@tsql", commandText) |> ignore
        cmd.ExecuteQuery(fun cursor ->
            let user_type_id = cursor.TryGetValue "user_type_id"
            let system_type_id = cursor.["system_type_id"] |> unbox<int>

            { 
                Column.Name = string cursor.["name"]
                TypeInfo = findTypeInfoBySqlEngineTypeId (this.ConnectionString, system_type_id, user_type_id)
                Nullable = unbox cursor.["is_nullable"]
                MaxLength = cursor.["max_length"] |> unbox<int16> |> int
                ReadOnly = not( cursor.GetValueOrDefault("is_updateable", true))
                Identity = cursor.GetValueOrDefault("is_identity_column", false)
                PartOfUniqueKey = cursor.GetValueOrDefault( "is_part_of_unique_key", false)
                DefaultConstraint = null
                Description = null
            }
        )
        |> Seq.toList 

    member internal this.FallbackToSETFMONLY(commandText, commandType, parameters: Parameter list) = 
        assert (this.State = ConnectionState.Open)
        
        use cmd = new SqlCommand(commandText, this, CommandType = commandType)
        for p in parameters do
            cmd.Parameters.Add(p.Name, p.TypeInfo.SqlDbType) |> ignore
        use reader = cmd.ExecuteReader(CommandBehavior.SchemaOnly)
        match reader.GetSchemaTable() with
        | null -> []
        | columnSchema -> 
            [
                for row in columnSchema.Rows do
                    yield { 
                        Column.Name = unbox row.["ColumnName"]
                        TypeInfo =
                            let t = Enum.Parse(typeof<SqlDbType>, string row.["ProviderType"]) |> unbox
                            findTypeInfoByProviderType(this.ConnectionString, t)
                        Nullable = unbox row.["AllowDBNull"]
                        MaxLength = unbox row.["ColumnSize"]
                        ReadOnly = unbox row.["IsAutoIncrement"] || unbox row.["IsReadOnly"]
                        Identity = unbox row.["IsAutoIncrement"]
                        PartOfUniqueKey = false
                        DefaultConstraint = null
                        Description = null
                    }
            ]

    member internal this.LoadDataTypesMap() = 
        if not <| dataTypeMappings.ContainsKey this.ConnectionString
        then
            assert (this.State = ConnectionState.Open)
            let providerTypes = 
                dict [| 
                    for row in this.GetSchema("DataTypes").Rows do
                        let fullTypeName = string row.["TypeName"]
                        let typeName, clrType = 
                            match fullTypeName.Split(',') |> List.ofArray with
                            | [name] -> name, string row.["DataType"]
                            | name::_ -> name, fullTypeName
                            | [] -> failwith "Unaccessible"

                        let isFixedLength = 
                            if row.IsNull("IsFixedLength") 
                            then None 
                            else row.["IsFixedLength"] |> unbox |> Some

                        let providedType = unbox row.["ProviderDbType"]
                        if providedType <> int SqlDbType.Structured
                        then 
                            yield typeName, (providedType, clrType, isFixedLength)
                |]

            let runningOnMono = try System.Type.GetType("Mono.Runtime") <> null with e -> false 
            let sqlEngineTypes = [|
                use cmd = new SqlCommand("
                    SELECT t.name, ISNULL(assembly_class, t.name) as full_name, t.system_type_id, t.user_type_id, t.is_table_type, s.name as schema_name, t.is_user_defined
                    FROM sys.types AS t
	                    JOIN sys.schemas AS s ON t.schema_id = s.schema_id
	                    LEFT JOIN sys.assembly_types ON t.user_type_id = sys.assembly_types.user_type_id
                    ", this) 
                use reader = cmd.ExecuteReader()
                while reader.Read() do
                    // #105 - disable assembly types when running on mono, because GetSchema() doesn't return these types on mono. 
                    let systemTypeId = unbox<byte> reader.["system_type_id"] 
                    if not runningOnMono || systemTypeId <> 240uy then
                      yield 
                        string reader.["name"], 
                        string reader.["full_name"], 
                        int systemTypeId, 
                        unbox<int> reader.["user_type_id"], 
                        unbox reader.["is_table_type"], 
                        string reader.["schema_name"], 
                        unbox reader.["is_user_defined"]
            |]

            let typeInfos = [|
                for name, full_name, system_type_id, user_type_id, is_table_type, schema_name, is_user_defined in sqlEngineTypes do
                    let providerdbtype, clrType, isFixedLength = 
                        match providerTypes.TryGetValue(full_name) with
                        | true, value -> value
                        | false, _ when full_name = "sysname" -> 
                            providerTypes.["nvarchar"]
                        | false, _ when is_user_defined && not is_table_type ->
                            let system_type_name = 
                                sqlEngineTypes 
                                |> Array.pick (fun (typename', _, system_type_id', _, _, _, is_user_defined') -> if system_type_id = system_type_id' && not is_user_defined' then Some typename' else None)
                            providerTypes.[system_type_name]
                        | false, _ when is_table_type -> 
                            int SqlDbType.Structured, "", None
                        | _ -> failwith ("Unexpected type: " + full_name)

                    let clrTypeFixed = if system_type_id = 48 (*tinyint*) then typeof<byte>.FullName else clrType

                    let columns = 
                        lazy 
                            if is_table_type
                            then
                                [|
                                    use cmd = new SqlCommand("
                                        SELECT c.name, c.system_type_id, c.user_type_id, c.is_nullable, c.max_length, c.is_identity, c.is_computed
                                        FROM sys.table_types AS tt
                                        INNER JOIN sys.columns AS c ON tt.type_table_object_id = c.object_id
                                        WHERE tt.user_type_id = @user_type_id
                                        ORDER BY column_id")
                                    cmd.Parameters.AddWithValue("@user_type_id", user_type_id) |> ignore
                                    use _ = this.UseLocally()
                                    cmd.Connection <- this
                                    use reader = cmd.ExecuteReader()
                                    while reader.Read() do 
                                        let user_type_id = reader.TryGetValue "user_type_id"
                                        let stid = reader.["system_type_id"] |> unbox<byte> |> int
                                        yield {
                                            Column.Name = string reader.["name"]
                                            TypeInfo = findTypeInfoBySqlEngineTypeId(this.ConnectionString, stid, user_type_id)
                                            Nullable = unbox reader.["is_nullable"]
                                            MaxLength = reader.["max_length"] |> unbox<int16> |> int
                                            ReadOnly = unbox reader.["is_identity"] || unbox reader.["is_computed"]
                                            Identity = unbox reader.["is_identity"] 
                                            PartOfUniqueKey = false
                                            DefaultConstraint = null
                                            Description = null
                                        }
                                |] 
                            else
                                Array.empty
                    yield {
                        TypeName = name
                        Schema = schema_name
                        SqlEngineTypeId = system_type_id
                        UserTypeId = user_type_id
                        SqlDbTypeId = providerdbtype
                        IsFixedLength = isFixedLength
                        ClrTypeFullName = clrTypeFixed
                        UdttName = if is_table_type then full_name else ""
                        TableTypeColumns = columns
                    }
            |]

            dataTypeMappings.Add( this.ConnectionString, typeInfos)

