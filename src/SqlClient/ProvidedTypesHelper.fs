namespace ProviderImplementation

open ProviderImplementation.ProvidedTypes

module internal ProvidedTypesHelper =
  let inline makeCtor parameters code =
      let ctor = ProvidedConstructor parameters
      ctor.InvokeCode <- code
      ctor
  let inline makeParamWithDefault n t d = ProvidedParameter(n, t, optionalValue = d)
  let inline makeParam n t = ProvidedParameter(n, t)

  

