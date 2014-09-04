// Copyright 2014 Lawrence Kesteloot

open System.Data

// Represents an entry in the FAQ.
type Entry = { question : string; answer : string; }

// Returns a list of Entry objects.
let readEntries pathname =
    // Accumulate the entries as we go. We put the new line into the existing
    // head of the list. We set the question in the head if it's a question line.
    // If the list already has a question, then we start a new entry.
    let parseEntries entries (line:string) =
        let question =
            if (line.StartsWith("# "))
                then (line.Substring(2))
                else ""
        match entries with
        | entry :: rest ->
            if entry.question <> ""
                then { question = question; answer = line; } :: entry :: rest
                else { question = question; answer = line + "\n" + entry.answer; } :: rest
        | [] -> [ { question = question; answer = line; } ]

    System.IO.File.ReadLines(pathname)
        |> List.ofSeq
        |> List.rev
        |> List.fold parseEntries []

// Removes all questions and answers from the database.
let clearDatabase (dbcon:IDbConnection) =
    Db.executeCommand dbcon "DELETE FROM answer"
    Db.executeCommand dbcon "DELETE FROM question"

// Imports an Entry object into the database.
let importEntry (dbcon:IDbConnection) entry =
    // Insert the answer and get its ID.
    let dbcmd = dbcon.CreateCommand()
    dbcmd.CommandText <- "INSERT INTO answer (answer_text) VALUES (@AnswerText) RETURNING id"
    Db.addParameter dbcmd "AnswerText" entry.answer
    let answerId = dbcmd.ExecuteScalar()

    // Insert the question and refer to the answer ID.
    let dbcmd = dbcon.CreateCommand()
    dbcmd.CommandText <- "INSERT INTO question (question_text, answer_id) VALUES (@QuestionText, @AnswerId)"
    Db.addParameter dbcmd "QuestionText" entry.question
    Db.addParameter dbcmd "AnswerId" answerId
    dbcmd.ExecuteNonQuery() |> ignore

    printfn "Inserted: %s" entry.question

// Imports a list of Entry objects into the database.
let importEntries (dbcon:IDbConnection) entries =
    entries |> List.iter (importEntry dbcon)

[<EntryPoint>]
let main argv =
    let dbcon = Db.makeConnection
    let entries = readEntries "db"
    clearDatabase dbcon
    importEntries dbcon entries
    0

