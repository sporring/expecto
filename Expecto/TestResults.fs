module Expecto.TestResults

open System.Globalization
open System.IO
open System.Reflection
open System.Xml.Linq
open System.Xml

let assemblyName = Assembly.GetEntryAssembly().GetName().Name

let private xmlSave fileName (doc: XDocument) =
  let path = Path.GetFullPath fileName
  Path.GetDirectoryName path
  |> Directory.CreateDirectory
  |> ignore
  let settings = XmlWriterSettings(CheckCharacters = false)
  use writer = XmlWriter.Create(path, settings)
  doc.Save writer

/// Generate test results using NUnit v2 schema.
let writeNUnitSummary file (summary: Impl.TestRunSummary) =
  // v3: https://github.com/nunit/docs/wiki/Test-Result-XML-Format
  // this impl is v2: http://nunit.org/docs/files/TestResult.xml
  let totalTests = summary.errored @ summary.failed @ summary.ignored @ summary.passed
  let testCaseElements =
    totalTests
    |> Seq.sortByDescending (fun (_,test) -> test.result.order,test.duration.TotalSeconds)
    |> Seq.map (fun (flatTest, test) ->
      let content: XObject[] =
        match test.result with
        | Impl.TestResult.Passed ->
          [|
            XAttribute(XName.Get "executed", "True")
            XAttribute(XName.Get "result", "Success")
            XAttribute(XName.Get "success", "True")
            XAttribute(XName.Get "time",
              System.String.Format(CultureInfo.InvariantCulture,
                "{0:0.000}", test.duration.TotalSeconds))
            XAttribute(XName.Get "asserts", "0")
          |]
        | Impl.TestResult.Error e ->
          [|
            XAttribute(XName.Get "executed", "True")
            XAttribute(XName.Get "result", "Failure")
            XAttribute(XName.Get "success", "False")
            XAttribute(XName.Get "time",
              System.String.Format(CultureInfo.InvariantCulture,
                "{0:0.000}", test.duration.TotalSeconds))
            XAttribute(XName.Get "asserts", "0")
            XElement(XName.Get "failure",
              [|
                XElement(XName.Get "message", XCData e.Message)
                XElement(XName.Get "stack-trace", XCData(e.ToString()))
              |])
          |]
        | Impl.TestResult.Failed msg ->
          [|
            XAttribute(XName.Get "executed", "True")
            XAttribute(XName.Get "result", "Failure")
            XAttribute(XName.Get "success", "False")
            XAttribute(XName.Get "time",
              System.String.Format(CultureInfo.InvariantCulture,
                "{0:0.000}", test.duration.TotalSeconds))
            XAttribute(XName.Get "asserts", "0")
            XElement(XName.Get "failure",
              XElement(XName.Get "message", XCData msg))
          |]
        | Impl.TestResult.Ignored msg ->
          [|
            XAttribute(XName.Get "executed", "False")
            XAttribute(XName.Get "result", "Ignored")
            XElement(XName.Get "reason",
              XElement(XName.Get "message", XCData msg))
          |]

      XElement(XName.Get "test-case",
        [|
          yield XAttribute(XName.Get "name", flatTest.name) :> XObject
          yield! content
        |]))
  let d = System.DateTime.Now
  let element =
    XElement(
      XName.Get "test-results",
      XAttribute(XName.Get "date", d.ToString("yyyy-MM-dd")),
      XAttribute(XName.Get "name", assemblyName),
      XAttribute(XName.Get "total", totalTests.Length),
      XAttribute(XName.Get "errors", summary.errored.Length),
      XAttribute(XName.Get "failures", summary.failed.Length),
      XAttribute(XName.Get "ignored", summary.ignored.Length),
      XAttribute(XName.Get "not-run", "0"),
      XAttribute(XName.Get "inconclusive", "0"),
      XAttribute(XName.Get "skipped", "0"),
      XAttribute(XName.Get "invalid", "0"),
      //XAttribute(XName.Get "date", sprintf "0"),
      XAttribute(XName.Get "time", d.ToString("HH:mm:ss")),
      XElement(XName.Get "environment",
        XAttribute(XName.Get "expecto-version", AssemblyInfo.AssemblyVersionInformation.AssemblyVersion),
        XAttribute(XName.Get "clr-version", string System.Environment.Version),
        XAttribute(XName.Get "os-version", System.Environment.OSVersion.VersionString),
        XAttribute(XName.Get "platform", System.Environment.OSVersion.Platform),
        XAttribute(XName.Get "cwd", System.Environment.CurrentDirectory),
        XAttribute(XName.Get "machine-name", System.Environment.MachineName),
        XAttribute(XName.Get "user", System.Environment.UserName),
        XAttribute(XName.Get "user-domain", System.Environment.UserDomainName)
      ),
      XElement(XName.Get "culture-info",
        XAttribute(XName.Get "current-culture", string CultureInfo.CurrentCulture),
        XAttribute(XName.Get "current-uiculture", string CultureInfo.CurrentUICulture)
      ),
      XElement(XName.Get "test-suite",
        XAttribute(XName.Get "type", "Assembly"),
        XAttribute(XName.Get "name", assemblyName),
        XAttribute(XName.Get "executed", "True"),
        XAttribute(XName.Get "result", if summary.successful then "Success" else "Failure"),
        XAttribute(XName.Get "success", if summary.successful then "True" else "False"),
        XAttribute(XName.Get "time",
          System.String.Format(CultureInfo.InvariantCulture,
            "{0:0.000}", summary.duration.TotalSeconds)),
        XAttribute(XName.Get "asserts", "0"),
        XElement(XName.Get "results", testCaseElements)
        )
      )

  element
  |> XDocument
  |> xmlSave file

let writeJUnitSummary file (summary: Impl.TestRunSummary) =

  // junit does not have an official xml spec
  // this is a minimal implementation to get gitlab to recognize the tests:
  // https://docs.gitlab.com/ee/ci/junit_test_reports.html
  let totalTests = summary.errored @ summary.failed @ summary.ignored @ summary.passed
  let testCaseElements =
    totalTests
    |> Seq.sortByDescending (fun (_,test) -> test.result.order,test.duration.TotalSeconds)
    |> Seq.map (fun (flatTest, test) ->
      let content: XObject[] =
        let makeMessageNode messageType (message: string) =
          XElement(XName.Get messageType,
            XAttribute(XName.Get "message", message))
        match test.result with
        | Impl.TestResult.Passed -> [||]
        | Impl.TestResult.Error e ->
          let message = makeMessageNode "error" e.Message
          message.Add(XCData(e.ToString()))
          [|message|]
        | Impl.TestResult.Failed msg -> [|makeMessageNode "failure" msg|]
        | Impl.TestResult.Ignored msg -> [|makeMessageNode "skipped" msg|]

      XElement(XName.Get "testcase",
        [|
          yield XAttribute(XName.Get "name", flatTest.name) :> XObject
          yield XAttribute(XName.Get "time",
            System.String.Format(CultureInfo.InvariantCulture,
              "{0:0.000}", test.duration.TotalSeconds)) :> XObject
          yield! content
        |]) :> XObject)
  let element =
    XElement(
      XName.Get "testsuites",
        XElement(XName.Get "testsuite",
          [|
            yield XAttribute(XName.Get "name", assemblyName) :> XObject
            yield! testCaseElements
          |])
      )

  element
  |> XDocument
  |> xmlSave file
