﻿namespace FSharp.Data

open System.Reflection
open System.Collections.Generic
open System

open Microsoft.FSharp.Core.CompilerServices

open ProviderImplementation.ProvidedTypes

open FSharp.Data.SqlClient

open System.Data
open System.Data.Common
open Microsoft.FSharp.Reflection
open Microsoft.FSharp.Quotations

[<TypeProvider>]
[<CompilerMessageAttribute("This API supports the FSharp.Data.SqlClient infrastructure and is not intended to be used directly from your code.", 101, IsHidden = true)>]
type SqlEnumProvider(config : TypeProviderConfig) as this = 
    inherit TypeProviderForNamespaces (config, assemblyReplacementMap=[("FSharp.Data.SqlClient.DesignTime", "FSharp.Data.SqlClient")], addDefaultProbingLocation=true)

    let nameSpace = this.GetType().Namespace
    let assembly = Assembly.GetExecutingAssembly()
    let providerType = ProvidedTypeDefinition(assembly, nameSpace, "SqlEnumProvider", Some typeof<obj>, hideObjectMethods = true, isErased = false)
    // let tempAssembly = ProvidedAssembly()
    // do tempAssembly.AddTypes [providerType]

    let cache = new Cache<ProvidedTypeDefinition>()

    static let allowedTypesForEnum = 
        HashSet [| 
            typeof<sbyte>; typeof<byte>; typeof<int16>; typeof<uint16>; typeof<int32>; typeof<uint32>; typeof<int64>; typeof<uint64>; typeof<char> 
        |]

    static let allowedTypesForLiteral = 
        let xs = HashSet [| typeof<float32>; typeof<float>; typeof<bigint>; typeof<decimal>; typeof<string> |]
        xs.UnionWith( allowedTypesForEnum)
        xs

    do 
        providerType.DefineStaticParameters(
            parameters = [ 
                ProvidedStaticParameter("Query", typeof<string>) 
                ProvidedStaticParameter("ConnectionStringOrName", typeof<string>) 
                ProvidedStaticParameter("Provider", typeof<string>, "Microsoft.Data.SqlClient") 
                ProvidedStaticParameter("ConfigFile", typeof<string>, "") 
                ProvidedStaticParameter("Kind", typeof<SqlEnumKind>, SqlEnumKind.Default) 
            ],             
            instantiationFunction = (fun typeName args ->   
                cache.GetOrAdd(typeName, lazy this.CreateRootType(typeName, unbox args.[0], unbox args.[1], unbox args.[2], unbox args.[3], unbox args.[4]))
            )        
        )

        providerType.AddXmlDoc """
<summary>Enumeration based on SQL query.</summary> 
<param name='Query'>SQL used to get the enumeration labels and values. A result set must have at least two columns. The first one is a label.</param>
<param name='ConnectionString'>String used to open a data connection.</param>
<param name='Provider'>Invariant name of a ADO.NET provider. Default is "Microsoft.Data.SqlClient".</param>
<param name='ConfigFile'>The name of the configuration file that’s used for connection strings at DESIGN-TIME. The default value is app.config or web.config.</param>
<param name='Kind'></param>
"""

        this.AddNamespace( nameSpace, [ providerType ])
    
    member internal this.CreateRootType( typeName, query, connectionStringOrName, provider, configFile, kind: SqlEnumKind) = 
        let tempAssembly = ProvidedAssembly()

        let providedEnumType = ProvidedTypeDefinition(tempAssembly, nameSpace, typeName, baseType = Some typeof<obj>, hideObjectMethods = true, isErased = false)
                
        let connStr, providerName = 
            match DesignTimeConnectionString.Parse(connectionStringOrName, config.ResolutionFolder, configFile) with
            | Literal value -> value, provider
            | NameInConfig(_, value, provider) -> value, provider

//#if !USE_SYSTEM_DATA_COMMON_DBPROVIDERFACTORIES
//        // not supported on netstandard 20?
//        raise ("DbProviderFactories not available" |> NotImplementedException) 
//#else
        let adoObjectsFactory = DbProviderFactories.GetFactory( providerName: string)
        use conn = adoObjectsFactory.CreateConnection() 
        conn.ConnectionString <- connStr
        conn.Open()

        use cmd = adoObjectsFactory.CreateCommand() 
        cmd.CommandText <- query
        cmd.Connection <- conn
        cmd.CommandType <- CommandType.Text

        use reader = cmd.ExecuteReader()
        if not reader.HasRows then failwith "Resultset is empty. At least one row expected." 
        let schema = reader.GetSchemaTable()

        let valueType, getValue = 
            
            let getValueType(row: DataRow) = 
                let t = Type.GetType( typeName = string row.["DataType"], throwOnError = true)
                if not( t.IsValueType || t = typeof<string>)
                then 
                    failwithf "Invalid type %s of column %O for value part. Only .NET value types and strings supported as value." t.FullName row.["ColumnName"]
                t

            match schema.Rows.Count with
            | 1 -> //natural keys case. Thanks Ami for introducing me to this idea.
                let valueType = getValueType schema.Rows.[0]
                let getValue (values : obj[]) = values.[0], Expr.Value (values.[0], valueType)

                valueType, getValue
            | 2 ->
                let valueType = getValueType schema.Rows.[1]
                let getValue (values : obj[]) = values.[1], Expr.Value (values.[1], valueType)

                valueType, getValue
            | _ ->
                let tupleItemTypes = 
                    schema.Rows
                    |> Seq.cast<DataRow>
                    |> Seq.skip 1
                    |> Seq.map getValueType
                
                let tupleType = tupleItemTypes |> Seq.toArray |> FSharpType.MakeTupleType
                let getValue (values : obj[]) =
                    let boxedValues = box values
                    let tupleExpression = 
                        (values |> Seq.skip 1, tupleItemTypes) 
                        ||> Seq.zip 
                        |> Seq.map Expr.Value 
                        |> Seq.toList 
                        |> Expr.NewTuple
                    boxedValues, tupleExpression
                tupleType, getValue

        let names, values = 
            [ 
                while reader.Read() do 
                    let rowValues = Array.zeroCreate reader.FieldCount
                    let count = reader.GetValues( rowValues)
                    assert (count = rowValues.Length)
                    let label = string rowValues.[0]
                    let value = getValue rowValues
                    yield label, value
            ] 
            |> List.unzip

        names 
        |> Seq.groupBy id 
        |> Seq.iter (fun (key, xs) -> if Seq.length xs > 1 then failwithf "Non-unique label %s." key)

        match kind with 
        | SqlEnumKind.CLI ->

            if not( allowedTypesForEnum.Contains( valueType))
            then failwithf "Enumerated types can only have one of the following underlying types: %A." [| for t in allowedTypesForEnum -> t.Name |]

            providedEnumType.SetBaseType typeof<Enum>
            providedEnumType.SetEnumUnderlyingType valueType

            (names, values)
            ||> List.map2 (fun name value -> ProvidedField.Literal(name, providedEnumType, fst value))
            |> providedEnumType.AddMembers

        | SqlEnumKind.UnitsOfMeasure ->

            for name in names do
                let units = ProvidedTypeDefinition( name, Some typedefof<obj>, isErased = false)
                units.AddCustomAttribute { 
                    new CustomAttributeData() with
                        member __.Constructor = typeof<MeasureAttribute>.GetConstructor [||]
                        member __.ConstructorArguments = upcast [||]
                        member __.NamedArguments = upcast [||]
                }
                providedEnumType.AddMember units

        | _ -> 
            let valueFields, setFieldValues = 
                (names, values) ||> List.map2 (fun name value -> 
                    if allowedTypesForLiteral.Contains valueType
                    then 
                        ProvidedField.Literal(name, valueType, fst value) :> FieldInfo, <@@ () @@>
                    else
                        let field = ProvidedField( name, valueType)
                        field.SetFieldAttributes( FieldAttributes.Public ||| FieldAttributes.InitOnly ||| FieldAttributes.Static)
                        field :> _, Expr.FieldSet(field, snd value)
                ) 
                |> List.unzip

            valueFields |> List.iter providedEnumType.AddMember

            let itemType = FSharpType.MakeTupleType([| typeof<string>; valueType |])
            let seqType = typedefof<_ seq>.MakeGenericType(itemType)
            let itemsField = ProvidedField( "Items", seqType)
            itemsField.SetFieldAttributes( FieldAttributes.Public ||| FieldAttributes.InitOnly ||| FieldAttributes.Static)
            providedEnumType.AddMember itemsField 
            
            let itemsExpr = Expr.NewArray(itemType, (names, values) ||> List.map2 (fun name value -> Expr.NewTuple([Expr.Value name; snd value ])))

            let typeInit = ProvidedConstructor([], IsTypeInitializer = true, invokeCode = fun _ -> 
                Expr.Sequential(
                    Expr.FieldSet(itemsField, Expr.Coerce(itemsExpr, seqType)),
                    setFieldValues |> List.reduce (fun x y -> Expr.Sequential(x, y))
                )
            )
            providedEnumType.AddMember typeInit 
            
            let tryParseImpl =
                this.GetType()
                    .GetMethod( "GetTryParseImpl", BindingFlags.NonPublic ||| BindingFlags.Static)
                    .MakeGenericMethod( valueType)
                    .Invoke( null, [| itemsExpr |])
                    |> unbox

            do  //TryParse
                let tryParse = 
                    ProvidedMethod(
                        methodName = "TryParse", 
                        parameters = [ 
                            ProvidedParameter("value", typeof<string>) 
                            ProvidedParameter("ignoreCase", typeof<bool>, optionalValue = false) // optional=false 
                        ], 
                        returnType = typedefof<_ option>.MakeGenericType( valueType), 
                        isStatic = true,
                        invokeCode = tryParseImpl
                    )

                providedEnumType.AddMember tryParse

            do  //Parse
                let parseImpl =
                    this.GetType()
                        .GetMethod( "GetParseImpl", BindingFlags.NonPublic ||| BindingFlags.Static)
                        .MakeGenericMethod( valueType)
                        .Invoke( null, [| itemsExpr; providedEnumType.FullName |])
                        |> unbox

                let parse = 
                    ProvidedMethod(
                        methodName = "Parse", 
                        parameters = [ 
                            ProvidedParameter("value", typeof<string>) 
                            ProvidedParameter("ignoreCase", typeof<bool>, optionalValue = false) 
                        ], 
                        returnType = valueType, 
                        isStatic = true, 
                        invokeCode = parseImpl
                    )

                providedEnumType.AddMember parse

            do  //TryFindName
                let findNameImpl = 
                    this.GetType()
                        .GetMethod( "GetTryFindName", BindingFlags.NonPublic ||| BindingFlags.Static)
                        .MakeGenericMethod( valueType)
                        .Invoke( null, [| itemsExpr |])
                        |> unbox
                
                let tryGetName = 
                    ProvidedMethod( methodName = "TryFindName", 
                        parameters = [ ProvidedParameter("value", valueType) ], 
                        returnType = typeof<string option>, 
                        isStatic = true,
                        invokeCode = findNameImpl
                    )

                providedEnumType.AddMember tryGetName

        tempAssembly.AddTypes [ providedEnumType ]
        providedEnumType

//#endif

    //Quotation factories
    
    static member internal GetTryParseImpl<'Value> items = 
        fun (args: _ list) ->
            <@@
                if String.IsNullOrEmpty (%%args.[0]) then nullArg "value"

                let comparer = 
                    if %%args.[1]
                    then StringComparer.InvariantCultureIgnoreCase
                    else StringComparer.InvariantCulture

                %%items |> Array.tryPick (fun (name: string, value: 'Value) -> if comparer.Equals(name, %%args.[0]) then Some value else None) 
            @@>

    static member internal GetParseImpl<'Value>(items, typeName) = 
        fun (args: _ list) ->
            let expr = (SqlEnumProvider.GetTryParseImpl<'Value> (items)) args
            <@@
                match %%expr with
                | Some(x : 'Value) -> x
                | None -> 
                    let errMsg = sprintf @"Cannot convert value ""%s"" to type ""%s""" %%args.[0] typeName
                    invalidArg "value" errMsg
            @@>

    static member internal GetTryFindName<'Value> items = 
        fun (args: _ list) ->
            <@@
                %%items |> Array.tryPick (fun (name: string, value: 'Value) -> if Object.Equals(value, (%%args.[0]: 'Value)) then Some name else None) 
            @@>
