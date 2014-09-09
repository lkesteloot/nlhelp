// Copyright 2014 Lawrence Kesteloot

open System
open System.Net
open System.Text
open System.IO
open System.Data

let staticRoot = @"static"
let host = "http://localhost:8080/"

// Represents an entry and all associated data.
type Entry = {
    question : string
    answer : string
    words : string list
    wordCount : int
}

// Converts a null string to an empty string.
let notNullString = function
    | null -> ""
    | s -> s

// Escape a JSON string.
let escapeJson (s:string) =
    s.Replace("\\", "\\\\").
        Replace("\"", "\\\"").
        Replace("/", "\\/").
        Replace("\b", "\\b").
        Replace("\n", "\\n").
        Replace("\r", "\\r").
        Replace("\t", "\\t")

// Removes possible ? and subsequent text.
let stripQuery (path:string) =
    match path.IndexOf("?") with
    | -1 -> path
    | index -> path.Substring(0, index)

// Returns a redirection.
let handleRedirection path =
    printfn "Redirecting to %s" path
    (int HttpStatusCode.Redirect, "", path)

// Loads all entries from the database and returns a list of Entry objects.
let loadEntries (dbcon:IDbConnection) =
    let dbcmd = dbcon.CreateCommand()
    dbcmd.CommandText <- "SELECT question_text, answer_text
                          FROM question JOIN answer ON question.answer_id = answer.id"
    let decodeRow (row:IDataRecord) =
        let question = row.GetString(0)
        let answer = row.GetString(1)
        let words = Nlp.wordsInString question
        {
            question = question
            answer = answer
            words = words
            wordCount = List.length words
        }
    Db.readerAsSeq (dbcmd.ExecuteReader())
        |> Seq.map decodeRow
        |> Seq.toList

// Handling of static files.
module Static =
    let private detectMimeType (pathname:string) =
        match (Path.GetExtension(pathname).ToLower()) with
        | ".html" -> "text/html"
        | ".js" -> "text/javascript" // "application/javascript" is correct but chokes IE.
        | ".css" -> "text/css"
        | ".jpg" | ".jpeg" -> "image/jpeg"
        | ".gif" -> "image/gif"
        | ".png" -> "image/png"
        | _ ->
            printfn "WARNING: Unknown extension for file %s" pathname
            "text/plain"

    let private handleStaticFile (pathname:string) =
        if (File.Exists pathname)
            then (int HttpStatusCode.OK, detectMimeType pathname, File.ReadAllText(pathname))
            else (int HttpStatusCode.NotFound, "text/plain", "Page does not exist.")

    let rec private handleStaticDirectory (path:string) =
        if (path.EndsWith("/"))
            then handleRequest (path + "index.html")
            else handleRedirection (path + "/")

    and handleRequest path =
        // Force it relative by adding "." in front:
        let pathname = Path.Combine(staticRoot, "." + path)
        printfn "Static file: %s" pathname
        if (Directory.Exists pathname)
            then handleStaticDirectory path
            else handleStaticFile pathname

// Handling of search requests.
module Search =
    type SearchResponse = {
        query: string
        hits: Entry seq
    }

    // Convert an Entry object to JSON.
    let makeJsonEntry entry =
        @"{""answer"":""" + escapeJson entry.answer + @"""}"

    // Convert a search response to JSON.
    let makeJsonSearchResponse (response:SearchResponse) =
        let entriesJson =
            response.hits
                |> Seq.map makeJsonEntry
                |> String.concat ", "
        @"{""query"":""" + escapeJson response.query +
            @""", ""entries"":[" + entriesJson + @"]}"

    // Returns whether the entry question contains the given term.
    let containsTerm word entry = List.exists ((=) word) entry.words

    // Compute the IDF for a given word.
    let computeIdf entries word =
        let numerator = List.length entries
        let denominator =
            entries
                |> List.filter (containsTerm word)
                |> List.length
        if denominator = 0 then 0.0 else log (float numerator / float denominator)

    // Compute a rating for an entry and a word. Higher is better. Returns
    // the rating.
    let rateEntryForWord entry (word, idf) =
        let countInDoc = entry.words |> List.filter ((=) word) |> List.length
        let tf = float countInDoc / float entry.wordCount
        tf*idf

    // Compute a rating for an entry given a set of query words. Higher is
    // better. Returns an (entry,rating) tuple.
    let rateEntry wordsAndIdfs entry =
        let rating = List.sumBy (rateEntryForWord entry) wordsAndIdfs
        (entry, rating)

    // Return the best "limit" entries for the given query.
    let findBestEntries entries query limit =
        let queryWords = Nlp.wordsInString query
        let idfs = List.map (computeIdf entries) queryWords
        let wordsAndIdfs = List.zip queryWords idfs
        entries
            |> List.map (rateEntry wordsAndIdfs)
            |> List.sortBy (fun (_, rating) -> rating)
            |> List.map (fun (entry, _) -> entry)
            |> List.rev
            |> Seq.ofList
            |> Seq.truncate limit

    // Handle queries to the /search URL.
    let handleRequest dbcon entries query =
        printfn "Query: %s" query
        let response = {
            query = query
            hits = findBestEntries entries query 10
        }
        (200, "application/json", makeJsonSearchResponse response)

module Website =
    // Handle GET requests.
    let private handleGetRequest dbcon entries (req:HttpListenerRequest) =
        match stripQuery req.RawUrl with
        | "/search" -> Search.handleRequest dbcon entries (notNullString (req.QueryString.Get("q")))
        | path -> Static.handleRequest path

    // Generic request handler.
    let handleRequest dbcon entries (req:HttpListenerRequest) =
        match req.HttpMethod.ToUpper() with
        | "GET" -> handleGetRequest dbcon entries req
        | _ -> (405, "text/plain", "Method not allowed")

module Http =
    // Write the response body.
    let private writeBody (resp:HttpListenerResponse) (statusCode:int) (contentType:string) (body:string) =
        let asciiBody = Encoding.ASCII.GetBytes(body)
        resp.StatusCode <- statusCode
        resp.ContentType <- contentType
        resp.ContentLength64 <- int64 asciiBody.Length
        resp.OutputStream.Write(asciiBody, 0, asciiBody.Length)
        resp.OutputStream.Close()

    // 302 Redirect to the location.
    let private redirectTo (resp:HttpListenerResponse) location =
        resp.Redirect(location)
        resp.OutputStream.Close()

    // Register a listener for HTTP requests.
    let registerListener (handler:(HttpListenerRequest->HttpListenerResponse->Async<unit>)) =
        let hl = new HttpListener()
        hl.Prefixes.Add host
        hl.Start()
        let task = Async.FromBeginEnd(hl.BeginGetContext, hl.EndGetContext)
        async {
            while true do
                let! context = task
                Async.Start(handler context.Request context.Response)
        } |> Async.Start
        printfn "Listening on %s" host

    // Listen for requests and create a response.
    let listener dbcon entries req resp =
        async {
            let statusCode, contentType, body = Website.handleRequest dbcon entries req
            printfn "%d %s %s" statusCode contentType req.RawUrl
            if statusCode >= 300 && statusCode < 400
                then redirectTo resp body
                else writeBody resp statusCode contentType body 
        }

[<EntryPoint>]
let main argv =
    let dbcon = Db.makeConnection
    let entries = loadEntries dbcon
    printfn "Read %d entries." (List.length entries)
    Http.registerListener (Http.listener dbcon entries)
    printfn "Press <ENTER> to quit."
    Console.ReadLine() |> ignore
    dbcon.Close()
    0
