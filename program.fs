open System
open System.Net
open System.Text
open System.IO

let siteRoot = @"static"
let host = "http://localhost:8080/"

let createListener (handler:(HttpListenerRequest->HttpListenerResponse->Async<unit>)) =
    let hl = new HttpListener()
    hl.Prefixes.Add host
    hl.Start()
    let task = Async.FromBeginEnd(hl.BeginGetContext, hl.EndGetContext)
    async {
        while true do
            let! context = task
            Async.Start(handler context.Request context.Response)
    } |> Async.Start

let rec handleStaticFile path =
    // Force it relative by adding "." in front:
    let file = Path.Combine(siteRoot, "." + path)
    printfn "Static file: %s" file
    if (Directory.Exists file)
        // XXX If ends with /, find, otherwise redirect.
        then handleStaticFile (path + "/index.html")
        elif (File.Exists file)
            then (200, "text/html", File.ReadAllText(file))
            else (404, "text/plain", "File does not exist!")

// Removes possible ? and subsequent text.
let stripQuery (path:string) =
    let index = path.IndexOf("?")
    if index >= 0
        then path.Substring(0, index)
        else path

// Converts a null string to an empty string.
let notNullString (s:string) =
    if s = null
        then ""
        else s

type SearchResponse = {
    Query: string
    Text: string
}

// Escape a JSON string.
let escapeJson (s:string) =
    s.Replace("\\", "\\\\").
        Replace("\"", "\\\"").
        Replace("/", "\\/").
        Replace("\b", "\\b").
        Replace("\n", "\\n").
        Replace("\r", "\\r").
        Replace("\t", "\\t")

// Convert a response to JSON.
let makeJsonSearchResponse (response:SearchResponse) =
    @"{""query"":""" + escapeJson response.Query +
        @""", ""text"":""" + escapeJson response.Text + @"""}"

let handleSearch query =
    printfn "Query: %s" query
    let response = {
        Query = query
        Text = "Hey hey"
    }
    (200, "application/json", makeJsonSearchResponse response)

let handleGetRequest (req:HttpListenerRequest) =
    let path = stripQuery req.RawUrl
    printfn "Path: %s" path
    if path = "/search"
        then handleSearch (notNullString (req.QueryString.Get("q")))
        else handleStaticFile path

let handleRequest (req:HttpListenerRequest) =
    if req.HttpMethod.ToUpper() <> "GET"
        then (405, "text/plain", "Method not allowed")
        else handleGetRequest req

createListener (fun req resp ->
    async {
        let code, contentType, body = handleRequest req
        let asciiBody = Encoding.ASCII.GetBytes(body)
        resp.ContentType <- contentType
        resp.OutputStream.Write(asciiBody, 0, asciiBody.Length)
        resp.OutputStream.Close()
    })

Console.ReadLine() |> ignore
