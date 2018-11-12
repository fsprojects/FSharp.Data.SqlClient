[<AutoOpen>]
module FSharp.Data.SqlClient.Extensions

open System
open System.Data
open System.Collections.Generic
open System.IO
open System.Threading.Tasks
open System.Data.SqlClient
open FSharp.Data

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

type internal RoutineType = StoredProcedure | TableValuedFunction | ScalarValuedFunction

type internal Routine = {
    Type: RoutineType
    Schema: string
    Name: string
    Definition: string
    Description: string option
    BaseObject: string * string
}   with

    member this.TwoPartName = this.Schema, this.Name

    member this.IsStoredProc = this.Type = StoredProcedure 
    
    member this.ToCommantText(parameters: Parameter list) = 
        let twoPartNameIdentifier = sprintf "%s.%s" <|| this.TwoPartName
        match this.Type with 
        | StoredProcedure -> twoPartNameIdentifier
        | TableValuedFunction -> 
            parameters |> List.map (fun p -> p.Name) |> String.concat ", " |> sprintf "SELECT * FROM %s(%s)" twoPartNameIdentifier
        | ScalarValuedFunction ->     
            parameters |> List.map (fun p -> p.Name) |> String.concat ", " |> sprintf "SELECT %s(%s)" twoPartNameIdentifier

let internal providerTypes = 
    dict [
        // exact numerics
        "bigint", (SqlDbType.BigInt, "System.Int64", true)
        "bit", (SqlDbType.Bit, "System.Boolean", true) 
        "decimal", (SqlDbType.Decimal, "System.Decimal", true) 
        "int", (SqlDbType.Int, "System.Int32", true)
        "money", (SqlDbType.Money, "System.Decimal", true) 
        "numeric", (SqlDbType.Decimal, "System.Decimal", true) 
        "smallint", (SqlDbType.SmallInt, "System.Int16", true)
        "smallmoney", (SqlDbType.SmallMoney, "System.Decimal", true) 
        "tinyint", (SqlDbType.TinyInt, "System.Byte", true)

        // approximate numerics
        "float", (SqlDbType.Float, "System.Double", true) // This is correct. SQL Server 'float' type maps to double
        "real", (SqlDbType.Real, "System.Single", true)

        // date and time
        "date", (SqlDbType.Date, "System.DateTime", true)
        "datetime", (SqlDbType.DateTime, "System.DateTime", true)
        "datetime2", (SqlDbType.DateTime2, "System.DateTime", true)
        "datetimeoffset", (SqlDbType.DateTimeOffset, "System.DateTimeOffset", true)
        "smalldatetime", (SqlDbType.SmallDateTime,  "System.DateTime", true)
        "time", (SqlDbType.Time, "System.TimeSpan", true)

        // character strings
        "char", (SqlDbType.Char, "System.String", true)
        "text", (SqlDbType.Text, "System.String", false)
        "varchar", (SqlDbType.VarChar, "System.String", false)

        // unicode character strings
        "nchar", (SqlDbType.NChar, "System.String", true)
        "ntext", (SqlDbType.NText, "System.String", false)
        "nvarchar", (SqlDbType.NVarChar, "System.String", false)
        "sysname", (SqlDbType.NVarChar, "System.String", false)

        // binary
        "binary", (SqlDbType.Binary, "System.Byte[]", true)
        "image", (SqlDbType.Image, "System.Byte[]", false)
        "varbinary", (SqlDbType.VarBinary, "System.Byte[]", false)

        //spatial
        "geography", (SqlDbType.Udt, "Microsoft.SqlServer.Types.SqlGeography, Microsoft.SqlServer.Types", false)
        "geometry", (SqlDbType.Udt, "Microsoft.SqlServer.Types.SqlGeometry, Microsoft.SqlServer.Types", false)

        //other
        "hierarchyid", (SqlDbType.Udt, "Microsoft.SqlServer.Types.SqlHierarchyId, Microsoft.SqlServer.Types", false)
        "sql_variant", (SqlDbType.Variant, "System.Object", false)

        "timestamp", (SqlDbType.Timestamp, "System.Byte[]", true)  // note: rowversion is a synonym but SQL Server stores the data type as 'timestamp'
        "uniqueidentifier", (SqlDbType.UniqueIdentifier, "System.Guid", true)
        "xml", (SqlDbType.Xml, "System.String", false)

        //TODO 
        //"cursor", typeof<TODO>
        //"table", typeof<TODO>
    ]


type SqlConnection with

 //address an issue when regular Dispose on SqlConnection needed for async computation 
 //wipes out all properties like ConnectionString in addition to closing connection to db
   
    member internal this.CheckVersion() = 
        assert (this.State = ConnectionState.Open)
        let majorVersion = this.ServerVersion.Split('.').[0]
        if int majorVersion < 11 
        then failwithf "Minimal supported major version is 11 (SQL Server 2012 and higher or Azure SQL Database). Currently used: %s" this.ServerVersion

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
                "fn_listextendedproperty ('MS_Description', 'schema', BaseObjectSchema, ROUTINE_TYPE, BaseObjectName, default, default)" 

        let getRoutinesQuery = 
            sprintf "
                WITH ExplicitRoutines AS
                (
	                SELECT 
		                ROUTINE_SCHEMA AS [Schema]
		                ,ROUTINE_NAME AS Name
		                ,ROUTINE_TYPE
		                ,DATA_TYPE
		                ,ROUTINE_SCHEMA AS BaseObjectSchema
		                ,ROUTINE_NAME AS BaseObjectName
	                FROM 
		                INFORMATION_SCHEMA.ROUTINES 
                ),
                Synonyms AS
                (
	                SELECT 
		                OBJECT_SCHEMA_NAME(object_id) AS [Schema]
		                ,name AS Name
		                ,ROUTINE_TYPE
		                ,DATA_TYPE
		                ,ROUTINE_SCHEMA AS BaseObjectSchema
		                ,ROUTINE_NAME AS BaseObjectName
	                FROM 
		                sys.synonyms
		                JOIN INFORMATION_SCHEMA.ROUTINES ON 
			                OBJECT_ID(ROUTINES.ROUTINE_SCHEMA + '.' + ROUTINES.ROUTINE_NAME) = OBJECT_ID(base_object_name)
                )
                SELECT 
	                XS.[Schema]
	                ,XS.Name
	                ,RoutineSubType = 
		                CASE  
			                WHEN XS.DATA_TYPE = 'TABLE' THEN 'TableValuedFunction'
			                WHEN XS.DATA_TYPE IS NULL THEN 'StoredProcedure'
			                ELSE 'ScalarValuedFunction'
		                END
	                ,ISNULL( OBJECT_DEFINITION( OBJECT_ID( XS.BaseObjectSchema + '.' + XS.BaseObjectName)), '') AS [Definition]
	                ,XS.BaseObjectSchema
	                ,XS.BaseObjectName
	                ,[Description] = XProp.Value
                FROM 
	                (
		                SELECT * FROM ExplicitRoutines
		                UNION ALL
		                SELECT * FROM Synonyms
	                ) AS XS
                    OUTER APPLY %s AS XProp
                WHERE	
	                [Schema] = @schema            
            " descriptionSelector 

        use cmd = new SqlCommand(getRoutinesQuery, this)
        cmd.Parameters.AddWithValue("@schema", schema) |> ignore

        cmd.ExecuteQuery(fun x ->
            let schema, name = unbox x.["Schema"], unbox x.["Name"]
            let definition = unbox x.["Definition"]
            let description = x.TryGetValue( "Description")
            let routineType =  
                match string x.["RoutineSubType"] with
                | "TableValuedFunction" -> TableValuedFunction
                | "StoredProcedure" -> StoredProcedure
                | "ScalarValuedFunction" -> ScalarValuedFunction
                | unexpected -> failwithf "Unexpected database routine type: %s." unexpected

            { 
                Type = routineType 
                Schema = schema
                Name = name
                Definition = definition
                Description = description 
                BaseObject = unbox x.["BaseObjectSchema"], unbox x.["BaseObjectName"]
            }
        ) 
        |> Seq.toArray
            
    member internal this.GetParameters( routine: Routine, isSqlAzure, useReturnValue) =      
        assert (this.State = ConnectionState.Open)

        let paramDefaults = Task.Factory.StartNew( fun() ->

            let parser = Microsoft.SqlServer.TransactSql.ScriptDom.TSql140Parser( true)
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
                ,max_length
                ,precision
                ,scale
	            ,description = ISNULL( XProp.Value, '')
            FROM sys.parameters AS p
                OUTER APPLY %s AS XProp
            WHERE
                p.Name <> '' 
                AND OBJECT_ID('%s.%s') = object_id" descriptionSelector <|| routine.BaseObject

        [
            use cmd = new SqlCommand( query, this)
            use cursor = cmd.ExecuteReader()
            while cursor.Read() do
                let name = string cursor.["name"]
                let direction = 
                    if unbox cursor.["suggested_is_output"]
                    then 
                        ParameterDirection.Output
                    else 
                        assert(unbox cursor.["suggested_is_input"])
                        ParameterDirection.Input 

                let system_type_id: int = unbox<byte> cursor.["suggested_system_type_id"] |> int
                let user_type_id = cursor.TryGetValue "suggested_user_type_id"

                let typeInfo = findTypeInfoBySqlEngineTypeId(this.ConnectionString, system_type_id, user_type_id)
                let defaultValue = match paramDefaults.Result.TryGetValue(name) with | true, value -> value | false, _ -> None
                let valueTypeWithNullDefault = typeInfo.IsValueType && defaultValue = Some(null)

                yield { 
                    Name = name
                    TypeInfo = typeInfo
                    Direction = direction
                    MaxLength = cursor.["max_length"] |> unbox<int16> |> int
                    Precision = unbox cursor.["precision"]
                    Scale = unbox cursor.["scale"]
                    DefaultValue = defaultValue
                    Optional = valueTypeWithNullDefault 
                    Description = string cursor.["description"]
                }

            if routine.IsStoredProc && useReturnValue 
            then
                yield {
                    Name = "@RETURN_VALUE"
                    TypeInfo = findTypeInfoByProviderType(this.ConnectionString, SqlDbType.Int)
                    Direction = ParameterDirection.ReturnValue
                    MaxLength = 4
                    Precision = 10uy
                    Scale = 0uy
                    DefaultValue = None
                    Optional = false 
                    Description = ""
                } 
        ]
        
    member internal this.GetTables( schema, isSqlAzure) = 
        assert (this.State = ConnectionState.Open)
        let descriptionSelector = 
            if isSqlAzure 
            then 
                "(SELECT NULL AS Value)"
            else 
                "fn_listextendedproperty ('MS_Description', 'schema', BaseObjectSchema, 'TABLE', BaseTableName, default, default)" 

        let getTablesQuery = sprintf "
           WITH TableSynonyms AS
            (
	            SELECT 
					Name 
					,SCHEMA_NAME(schema_id) AS [Schema]
					,TABLE_NAME AS BaseTableName
					,TABLE_SCHEMA AS BaseObjectSchema
	            FROM sys.synonyms
		            JOIN INFORMATION_SCHEMA.TABLES ON 
			            OBJECT_ID(TABLES.TABLE_SCHEMA + '.' + TABLES.TABLE_NAME) = OBJECT_ID(base_object_name) 
						AND TABLE_TYPE = 'BASE TABLE'
            ),
			Tables AS 
			(
				SELECT 
					TABLE_NAME AS Name
					,TABLE_SCHEMA AS [Schema]
					,TABLE_NAME AS BaseTableName
					,TABLE_SCHEMA AS BaseObjectSchema
	            FROM INFORMATION_SCHEMA.TABLES 
				WHERE TABLE_TYPE = 'BASE TABLE'
			)
            SELECT 
                *
	            ,DESCRIPTION = XProp.Value
            FROM 
                (
	                SELECT * FROM TableSynonyms
	                UNION ALL
					SELECT * FROM Tables
                ) AS _
                OUTER APPLY %s AS XProp
            WHERE 
	             [Schema] = '%s'" descriptionSelector schema
        use cmd = new SqlCommand(getTablesQuery, this)
        cmd.ExecuteQuery(fun x -> 
            string x.["Name"], 
            string x.["BaseTableName"], 
            string x.["BaseObjectSchema"], 
            x.TryGetValue( "DESCRIPTION") 
        ) |> Seq.toList

    member internal this.GetFullQualityColumnInfo commandText = 
        assert (this.State = ConnectionState.Open)
        use cmd = new SqlCommand("sys.sp_describe_first_result_set", this, CommandType = CommandType.StoredProcedure)
        cmd.Parameters.AddWithValue("@tsql", commandText) |> ignore
        cmd.ExecuteQuery(fun cursor ->
            let user_type_id = cursor.TryGetValue "user_type_id"
            let system_type_id = cursor.["system_type_id"] |> unbox<int>

            { 
                Column.Name       = string cursor.["name"]
                TypeInfo          = findTypeInfoBySqlEngineTypeId (this.ConnectionString, system_type_id, user_type_id)
                Nullable          = unbox cursor.["is_nullable"]
                MaxLength         = cursor.["max_length"] |> unbox<int16> |> int
                ReadOnly          = not( cursor.GetValueOrDefault("is_updateable", true))
                Identity          = cursor.GetValueOrDefault("is_identity_column", false)
                PartOfUniqueKey   = cursor.GetValueOrDefault( "is_part_of_unique_key", false)
                DefaultConstraint = null
                Description       = null
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
            
            let sqlEngineTypes = [|
                use cmd = new SqlCommand("
                    SELECT t.name, t.system_type_id, t.user_type_id, t.is_table_type, s.name as schema_name, t.is_user_defined
                    FROM sys.types AS t
	                    JOIN sys.schemas AS s ON t.schema_id = s.schema_id
                    ", this) 
                use reader = cmd.ExecuteReader()
                while reader.Read() do
                    let systemTypeId = unbox<byte> reader.["system_type_id"] 
                    yield 
                        string reader.["name"], 
                        int systemTypeId, 
                        unbox<int> reader.["user_type_id"], 
                        unbox reader.["is_table_type"], 
                        string reader.["schema_name"], 
                        unbox reader.["is_user_defined"]
            |]

            let typeInfos = [|
                for name, system_type_id, user_type_id, is_table_type, schema_name, is_user_defined in sqlEngineTypes do
                    let providerdbtype, clrTypeFullName, isFixedLength = 
                        match providerTypes.TryGetValue(name) with
                        | true, value -> value
                        | false, _ when is_user_defined && not is_table_type ->
                            let system_type_name = 
                                sqlEngineTypes 
                                |> Array.pick (fun (typename', system_type_id', _, _, _, is_user_defined') -> if system_type_id = system_type_id' && not is_user_defined' then Some typename' else None)
                            providerTypes.[system_type_name]                        
                        | false, _ when is_table_type -> 
                            SqlDbType.Structured, null, false
                        | _ -> failwith ("Unexpected type: " + name)

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
                        SqlDbType = providerdbtype
                        IsFixedLength = isFixedLength
                        ClrTypeFullName = clrTypeFullName
                        UdttName = if is_table_type then name else ""
                        TableTypeColumns = columns
                    }
            |]

            dataTypeMappings.Add( this.ConnectionString, typeInfos)

