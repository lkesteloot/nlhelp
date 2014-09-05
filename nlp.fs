// Copyright 2014 Lawrence Kesteloot

// Natural language processing methods.
module Nlp

open System
open System.Text.RegularExpressions

let punctuationRegex = (new Regex("[^a-zA-Z0-9]+"))

// Returns a list of words in the strings, in order. All punctuation is removed
// and all text is converted to lower case.
let wordsInString (s:string) =
    List.ofArray (punctuationRegex.Replace(s.ToLower(), " ").Split([|' '|], StringSplitOptions.RemoveEmptyEntries))

