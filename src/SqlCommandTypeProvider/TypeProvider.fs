namespace FSharp.Data.SqlClient

open System
open System.Data
open System.Data.SqlClient
open System.Reflection
open System.Collections.Generic
open System.Threading

open Microsoft.FSharp.Core.CompilerServices
open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Reflection

open Samples.FSharp.ProvidedTypes

type ResultType =
    | Tuples = 0
    | Records = 1
    | DataTable = 3

type ValueParameter = { Name: string
                        ClrTypeName: string
                        SqlDbTypeId: int
                        Direction: ParameterDirection }

type TableValuedColumn    = { Name: string
                              ClrTypeName: string
                              IsNullable: bool 
                              ColumnType: Type}

type TableValuedParameter = { Name: string
                              TableTypeName: string
                              SqlDbTypeId: int
                              PropertyType: Type
                              Columns: TableValuedColumn list}
type Parameter = 
     | Value of ValueParameter
     | TableValued of TableValuedParameter

[<TypeProvider>]
type public SqlCommandTypeProvider(config : TypeProviderConfig) as this = 
    inherit TypeProviderForNamespaces()

    let mutable watcher = null : IDisposable
    let toSqlParameter p = 
      match p with
      | Value vp ->
          <@@ SqlParameter(%%Expr.Value (vp.Name), %%Expr.Value(vp.SqlDbTypeId) |> enum, Direction = %%Expr.Value (vp.Direction)) @@>
      | TableValued tvp -> 
          <@@ let p = SqlParameter(%%Expr.Value (tvp.Name), %%Expr.Value (tvp.SqlDbTypeId) |> enum)
              p.TypeName <- %%Expr.Value (tvp.TableTypeName)
              p @@>


    let nameSpace = this.GetType().Namespace
    let assembly = Assembly.GetExecutingAssembly()
    let providerType = ProvidedTypeDefinition(assembly, nameSpace, "SqlCommand", Some typeof<obj>, HideObjectMethods = true)
    let invalidateE = new Event<EventHandler,EventArgs>()    
    do 
        providerType.DefineStaticParameters(
            parameters = [ 
                ProvidedStaticParameter("CommandText", typeof<string>) 
                ProvidedStaticParameter("ConnectionString", typeof<string>, "") 
                ProvidedStaticParameter("ConnectionStringName", typeof<string>, "") 
                ProvidedStaticParameter("CommandType", typeof<CommandType>, CommandType.Text) 
                ProvidedStaticParameter("ResultType", typeof<ResultType>, ResultType.Tuples) 
                ProvidedStaticParameter("SingleRow", typeof<bool>, false) 
                ProvidedStaticParameter("ConfigFile", typeof<string>, "") 
                ProvidedStaticParameter("DataDirectory", typeof<string>, "") 
            ],             
            instantiationFunction = this.CreateType
        )
        this.AddNamespace(nameSpace, [ providerType ])
    
    interface ITypeProvider with
        [<CLIEvent>]
        override this.Invalidate = invalidateE.Publish

    interface IDisposable with 
        member this.Dispose() = 
           if watcher <> null
           then try watcher.Dispose() with _ -> ()

    member internal this.CreateType typeName parameters = 
        let commandText : string = unbox parameters.[0] 
        let connectionStringProvided : string = unbox parameters.[1] 
        let connectionStringName : string = unbox parameters.[2] 
        let commandType : CommandType = unbox parameters.[3] 
        let resultType : ResultType = unbox parameters.[4] 
        let singleRow : bool = unbox parameters.[5] 
        let configFile : string = unbox parameters.[6] 
        let dataDirectory : string = unbox parameters.[7] 

        let resolutionFolder = config.ResolutionFolder
        let commandText, watcher' = 
            Configuration.ParseTextAtDesignTime(commandText, resolutionFolder, fun ()-> invalidateE.Trigger(this,EventArgs()))
        watcher' |> Option.iter (fun x -> watcher <- x)
        let designTimeConnectionString =  Configuration.GetConnectionString(resolutionFolder, connectionStringProvided, connectionStringName, configFile)
        
        using(new SqlConnection(designTimeConnectionString)) <| fun conn ->
            conn.Open()
            conn.CheckVersion()
            conn.LoadDataTypesMap()
       
        let isStoredProcedure = commandType = CommandType.StoredProcedure

        let providedCommandType = ProvidedTypeDefinition(assembly, nameSpace, typeName, baseType = Some typeof<obj>, HideObjectMethods = true)

        providedCommandType.AddMembersDelayed <| fun () -> 
            [
                let parameters = this.ExtractParameters(designTimeConnectionString, commandText, isStoredProcedure)

                yield! this.AddPropertiesForParameters(parameters) 
                let ctor = ProvidedConstructor([ProvidedParameter("connectionString", typeof<string>, optionalValue = Unchecked.defaultof<string>)])
                ctor.InvokeCode <- fun args -> 
                    <@@ 
                        let runTimeConnectionString = 
                            if String.IsNullOrEmpty(%%args.[0])
                            then
                                Configuration.GetConnectionString (resolutionFolder, connectionStringProvided, connectionStringName, configFile)
                            else 
                                %%args.[0]
                        do
                            if dataDirectory <> ""
                            then AppDomain.CurrentDomain.SetData("DataDirectory", dataDirectory)

                        let this = new SqlCommand(commandText, new SqlConnection(runTimeConnectionString)) 
                        this.Connection <- new SqlConnection(runTimeConnectionString)
                        this.CommandType <- commandType
                        this.Parameters.AddRange(
                          %%(Expr.NewArray(typeof<SqlParameter>, parameters |> List.map toSqlParameter))
                             )
                        this
                    @@>

                yield ctor :> MemberInfo    
            ]
        
        
        let outputColumns : _ list = this.GetOutputColumns(commandText, designTimeConnectionString)
        
        this.AddExecuteMethod(outputColumns, providedCommandType, resultType, singleRow, commandText) 
        
        let getSqlCommandCopy = ProvidedMethod("AsSqlCommand", [], typeof<SqlCommand>)
        getSqlCommandCopy.InvokeCode <- fun args ->
            <@@
                let self : SqlCommand = %%Expr.Coerce(args.[0], typeof<SqlCommand>)
                let clone = self.Clone()
                clone.Connection <- new SqlConnection(self.Connection.ConnectionString)
                clone
            @@>
        providedCommandType.AddMember getSqlCommandCopy          

        providedCommandType


    member this.GetOutputColumns(commandText, connectionString) = [
        use conn = new SqlConnection(connectionString)
        use cmd = new SqlCommand("sys.sp_describe_first_result_set", conn, CommandType = CommandType.StoredProcedure)
        cmd.Parameters.AddWithValue("@tsql", commandText) |> ignore
        conn.Open()
        use reader = cmd.ExecuteReader()
        while reader.Read() do
            let columnName = string reader.["name"]
            let sqlEngineTypeId = unbox reader.["system_type_id"]
            let detailedMessage = " Column name:" + columnName
            let isNullable : bool = unbox reader.["is_nullable"]
            let clrTypeName, _ =  mapSqlEngineTypeId(sqlEngineTypeId, detailedMessage)

            yield columnName, clrTypeName, unbox<int> reader.["column_ordinal"], isNullable
    ] 
    member this.ExtractParameters(connectionString, commandText, isStoredProcedure) : Parameter list =  [
            use conn = new SqlConnection(connectionString)
            conn.Open()

            let categorize name clrTypeName sqlDbType direction userTypeName = 
               let tableType = this.FindTableType(connectionString, userTypeName)
               match tableType with 
               | None -> 
                   Value { Name        = name
                           ClrTypeName = clrTypeName
                           SqlDbTypeId = sqlDbType
                           Direction   = direction }
               | Some (propertyType, cols) -> 
                   TableValued { Name = name
                                 TableTypeName = userTypeName
                                 SqlDbTypeId = sqlDbType
                                 PropertyType = propertyType
                                 Columns = cols }

            if isStoredProcedure
            then
                //quick solution for now. Maybe better to use conn.GetSchema("ProcedureParameters")
                use cmd = new SqlCommand(commandText, conn, CommandType = CommandType.StoredProcedure)
                SqlCommandBuilder.DeriveParameters cmd
                for p in cmd.Parameters do
                    // typeName is full namespace, ie AdventureWorks2012.dbo.myTableType. Only need the last part.
                    let userTypeName = if String.IsNullOrEmpty(p.TypeName) then "" else p.TypeName.Split('.') |> Seq.last
                    let clrTypeName = findBySqlDbType p.SqlDbType
                    yield categorize p.ParameterName clrTypeName (int p.SqlDbType) p.Direction userTypeName
            else
                use cmd = new SqlCommand("sys.sp_describe_undeclared_parameters", conn, CommandType = CommandType.StoredProcedure)
                cmd.Parameters.AddWithValue("@tsql", commandText) |> ignore
                use reader = cmd.ExecuteReader()
                while(reader.Read()) do
                    let paramName = string reader.["name"]
                    let sqlEngineTypeId = unbox<int> reader.["suggested_system_type_id"]
                    let userTypeName = let name = reader.GetOrdinal("suggested_user_type_name") in
                                       if reader.IsDBNull(name) then "" else reader.GetString (name)
                    
                    let detailedMessage = " Parameter name:" + paramName
                    let clrTypeName, sqlDbTypeId = mapSqlEngineTypeId(sqlEngineTypeId, detailedMessage)
                    let direction = 
                        let output = unbox reader.["suggested_is_output"]
                        let input = unbox reader.["suggested_is_input"]
                        if input && output then ParameterDirection.InputOutput
                        elif output then ParameterDirection.Output
                        else ParameterDirection.Input
                    yield categorize paramName clrTypeName sqlDbTypeId direction userTypeName    
        ]

    member this.FindTableType(connectionString, tableTypeName:string) = 
        let columnCommandText = "
           select c.*
           from sys.table_types as tt
           inner join sys.columns as c on tt.type_table_object_id = c.object_id
           where tt.name = @name
           order by column_id"

        if tableTypeName <> String.Empty then 
            use conn = new SqlConnection(connectionString)
            conn.Open()
            use cmd = new SqlCommand(columnCommandText, conn)
            cmd.Parameters.AddWithValue("@name", tableTypeName) |> ignore
            let cols = cmd.ExecuteReaderWith (fun r ->
               let name = r.["name"] |> unbox
               let error = sprintf "Table Type %s, column '%s'" tableTypeName name
               let systype = r.["system_type_id"] |> unbox<byte>
               let clrTypeName, sqlType = mapSqlEngineTypeId(systype |> int, error)
               let isNullable = r.["is_nullable"] |> unbox
               { Name = name
                 IsNullable = isNullable
                 ClrTypeName = clrTypeName
                 ColumnType = this.GetTypeForNullable(clrTypeName, isNullable) } ) |> List.ofSeq
            match cols.Length with 
            | 0 -> None
            | 1 -> let propType = typedefof<_ seq>.MakeGenericType cols.Head.ColumnType
                   Some(propType, cols)
            | x -> let tupletype = FSharpType.MakeTupleType(cols |> List.map(fun c -> c.ColumnType) |> Array.ofList)
                   let propType = typedefof<_ seq>.MakeGenericType tupletype
                   Some(propType, cols)
         else None

    member internal __.AddPropertiesForParameters(parameters: Parameter list) =  [
            for p in parameters do
               match p with
               | Value vp -> 
                  let paramName = Expr.Value vp.Name
                  
                  let propertyName = if vp.Direction = ParameterDirection.ReturnValue then "SpReturnValue" else vp.Name.Substring 1
                  let propertyType = Type.GetType vp.ClrTypeName
                  
                  let prop = ProvidedProperty(propertyName, propertyType = propertyType)
                  if vp.Direction = ParameterDirection.Output || vp.Direction = ParameterDirection.InputOutput || vp.Direction = ParameterDirection.ReturnValue
                  then 
                      prop.GetterCode <- fun args -> 
                          <@@ 
                              let sqlCommand : SqlCommand = %%Expr.Coerce(args.[0], typeof<SqlCommand>)
                              sqlCommand.Parameters.[%%paramName:string].Value
                          @@>
  
                  if vp.Direction = ParameterDirection.Input
                  then 
                      prop.SetterCode <- fun args -> 
                          <@@ 
                              let sqlCommand : SqlCommand = %%Expr.Coerce(args.[0], typeof<SqlCommand>)
                              sqlCommand.Parameters.[%%paramName:string].Value <- %%Expr.Coerce(args.[1], typeof<obj>)
                          @@>
                  yield prop :> MemberInfo
  
               | TableValued tvp ->
                  let propertyName = tvp.Name.Substring 1
                  let prop = ProvidedProperty(propertyName, propertyType = tvp.PropertyType)
                  let columnTypeNames = tvp.Columns |> List.map (fun c -> c.ClrTypeName)
                  let isNullable = tvp.Columns |> List.map (fun c -> c.IsNullable)
                  let columnLength = Expr.Value columnTypeNames.Length
                  let paramName = Expr.Value tvp.Name
                  let mapper = QuotationsFactory.MapOptionsToNullables(columnTypeNames,isNullable)
                  prop.SetterCode <- fun args -> 
                       <@@
                          ()
                          let sqlCommand : SqlCommand = %%Expr.Coerce(args.[0], typeof<SqlCommand>)
                          let table = new DataTable();
                          let xs = %%Expr.Coerce(args.[1], typeof<System.Collections.IEnumerable>) |> Seq.cast<obj>
                          for col in 0 .. (%%columnLength:int) - 1 do
                              table.Columns.Add() |> ignore
                          for x in xs do
                              let values = if (%%columnLength:int) = 1 then [|box x|] else FSharpValue.GetTupleFields(x)
                              (%%mapper : obj[] -> unit) values
                              table.Rows.Add(values) |> ignore 
                          sqlCommand.Parameters.[%%paramName:string].Value <- table
                       @@>
                  yield prop :> MemberInfo
        ]

    member internal __.GetExecuteNonQuery() = 
        let body (args :Expr list) =
            <@@
                async {
                    let sqlCommand = %%Expr.Coerce(args.[0], typeof<SqlCommand>) : SqlCommand
                    //open connection async on .NET 4.5
                    sqlCommand.Connection.Open()
                    use ensureConnectionClosed = sqlCommand.CloseConnectionOnly()
                    return! sqlCommand.AsyncExecuteNonQuery() 
                }
            @@>
        typeof<int>, body

    member internal __.GetTypeForNullable(columnType, isNullable) : Type = 
        let nakedType = Type.GetType columnType 
        if isNullable then typedefof<_ option>.MakeGenericType(nakedType) else nakedType

    member internal __.AddExecuteMethod(outputColumns: _ list, providedCommandType: ProvidedTypeDefinition, resultType, singleRow, commandText) = 
            
        let syncReturnType, executeMethodBody = 
            if outputColumns.IsEmpty
            then 
                this.GetExecuteNonQuery()
            elif resultType = ResultType.DataTable
            then 
                this.DataTable(providedCommandType, commandText, outputColumns, singleRow)
            else
                let rowType, executeMethodBody = 
                    if outputColumns.Length = 1
                    then
                        let _, column0TypeName, _, isNullable = outputColumns.Head
                        let column0Type = this.GetTypeForNullable(column0TypeName, isNullable)
                        column0Type, QuotationsFactory.GetBody("SelectOnlyColumn0", column0Type, singleRow, column0TypeName, isNullable)
                    elif resultType = ResultType.Tuples 
                    then 
                        this.Tuples(providedCommandType, outputColumns, singleRow)
                    else 
                        assert (resultType = ResultType.Records)
                        this.Records(providedCommandType, outputColumns, singleRow)

                //let returnType = if singleRow then rowType else ProvidedTypeBuilder.MakeGenericType(typedefof<_ seq>, [ rowType ])
                let returnType = 
                    match singleRow, rowType with
                    | true, _ -> rowType
                    | false, :? ProvidedTypeDefinition as providedType -> ProvidedTypeBuilder.MakeGenericType(typedefof<_ seq>, [ rowType ])     
                    | false, _ -> typedefof<_ seq>.MakeGenericType rowType     
                           
                returnType, executeMethodBody
                    
        let asyncReturnType = ProvidedTypeBuilder.MakeGenericType(typedefof<_ Async>, [ syncReturnType ])
        let asyncExecute = ProvidedMethod("AsyncExecute", [], asyncReturnType, InvokeCode = executeMethodBody)
        let execute = ProvidedMethod("Execute", [], syncReturnType)
        execute.InvokeCode <- fun args ->
            let runSync = ProvidedTypeBuilder.MakeGenericMethod(typeof<Async>.GetMethod("RunSynchronously"), [ syncReturnType ])
            let callAsync = Expr.Call (Expr.Coerce (args.[0], providedCommandType), asyncExecute, [])
            Expr.Call(runSync, [ Expr.Coerce (callAsync, asyncReturnType); Expr.Value option<int>.None; Expr.Value option<CancellationToken>.None ])

        providedCommandType.AddMembers [ asyncExecute; execute ]

    member internal this.Tuples(providedCommandType, outputColumns, singleRow) =
        let columnTypes, isNullableColumn, tupleItemTypes = 
            List.unzip3 [
                for _, typeName, _, isNullable in outputColumns do
                    yield typeName, isNullable, this.GetTypeForNullable(typeName, isNullable)
            ]

        let tupleType = tupleItemTypes |> List.toArray |> FSharpType.MakeTupleType

        let rowMapper = 
            let values = Var("values", typeof<obj[]>)
            let getTupleType = Expr.Call(typeof<Type>.GetMethod("GetType", [| typeof<string>|]), [ Expr.Value tupleType.AssemblyQualifiedName ])
            Expr.Lambda(values, Expr.Coerce(Expr.Call(typeof<FSharpValue>.GetMethod("MakeTuple"), [Expr.Var values; getTupleType]), tupleType))

        tupleType, QuotationsFactory.GetBody("GetTypedSequence", tupleType, rowMapper, singleRow, columnTypes, isNullableColumn)

    member internal this.Records(providedCommandType, outputColumns, singleRow) =
        let recordType = ProvidedTypeDefinition("Record", baseType = Some typeof<obj>, HideObjectMethods = true)
        for name, propertyTypeName, columnOrdinal, isNullable  in outputColumns do
            if name = "" then failwithf "Column #%i doesn't have name. Only columns with names accepted. Use explicit alias." columnOrdinal

            let property = ProvidedProperty(name, propertyType = this.GetTypeForNullable(propertyTypeName, isNullable))
            property.GetterCode <- fun args -> 
                <@@ 
                    let values : obj[] = %%Expr.Coerce(args.[0], typeof<obj[]>)
                    values.[%%Expr.Value (columnOrdinal - 1)]
                @@>

            recordType.AddMember property

        providedCommandType.AddMember recordType
        let getExecuteBody (args : Expr list) = 
            let columnTypes, isNullableColumn = List.unzip [ for _, propertyTypeName, _, isNullable in outputColumns -> propertyTypeName, isNullable ] 
            QuotationsFactory.GetTypedSequence(args.[0], <@ fun(values : obj[]) -> box values @>, singleRow, columnTypes, isNullableColumn)
                         
        upcast recordType, getExecuteBody
    
    member internal this.DataTable(providedCommandType, commandText, outputColumns, singleRow) =
        let rowType = ProvidedTypeDefinition("Row", Some typeof<DataRow>)
        for name, propertyTypeName, columnOrdinal, isNullable  in outputColumns do
            if name = "" then failwithf "Column #%i doesn't have name. Only columns with names accepted. Use explicit alias." columnOrdinal

            let nakedType = Type.GetType propertyTypeName

            let property = 
                if isNullable 
                then
                    ProvidedProperty(
                        name, 
                        propertyType= typedefof<_ option>.MakeGenericType nakedType,
                        GetterCode = QuotationsFactory.GetBody("GetNullableValueFromRow", nakedType, name),
                        SetterCode = QuotationsFactory.GetBody("SetNullableValueInRow", nakedType, name)
                    )
                else
                    ProvidedProperty(
                        name, 
                        propertyType = nakedType, 
                        GetterCode = (fun args -> <@@ (%%args.[0] : DataRow).[name] @@>),
                        SetterCode = fun args -> <@@ (%%args.[0] : DataRow).[name] <- %%Expr.Coerce(args.[1],  typeof<obj>) @@>
                    )

            rowType.AddMember property

        providedCommandType.AddMember rowType

        let body = QuotationsFactory.GetBody("GetTypedDataTable",  typeof<DataRow>, singleRow)
        let returnType = typedefof<_ DataTable>.MakeGenericType rowType

        returnType, body

