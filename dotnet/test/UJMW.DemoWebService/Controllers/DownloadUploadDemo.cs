using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Annotations;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;

namespace Security.Controllers {
  public class UjmwDtoSchemaFilter : ISchemaFilter {
    public void Apply(OpenApiSchema schema, SchemaFilterContext context) {
      //if (context.Type == typeof(MyDto)) // DTO auswählen
      //{

      //if (UjmwHostConfiguration.SideChannelSamlces == "") {
      //var cx =new schemafi

      //}
      //else {

      //}

      //TODO: das hier ausprogrammieren damit "_"-Property scheit im Swagger aussght!

      //schema.Properties.Remove("_");  //GEHT!!!!!


      //GEHT AUCH: für sinnvolle dokue
      schema.Properties["_"].Title = "UJMW-Sidechannel";
      schema.Properties["_"].Description = "Optional set of contextual indentifiers (DataFlow)";
      schema.Properties["_"].Example = OpenApiAnyFactory.CreateFromJson("{ \"System\":null,\"Silo\":null}");
      

      //schema.Example = null ;
      //}
    }
  }

  [SwaggerSchema("ddddddddddddd")]
  [SwaggerSchemaFilter(typeof(UjmwDtoSchemaFilter))]
  public class Dto {

    [DefaultValue(typeof(object),"{ \"OptionalProperty1\": \"some contextual information\"}")]

    public Dictionary<string,string> _ { get; set; }

    [SwaggerSchema("fffffffffffffffff")]
      public string foo { get; set; }
      //konvention  return stream out string fileName / out string mimeType
    }


  [Route("afs"),Tags("UIUIUI")]
  [ApiExplorerSettings(GroupName = "Fileaccess-Demo")]
  public class DownloadUploadDemo : Controller {


    [HttpGet(), Route("sss"),]
    public Dto AAAAAAAAAAAAAAAAAAAA() {
      return null;
    }



    [HttpGet()]
    public IActionResult Download() {
      Stream stream = System.IO.File.OpenRead("C:\\Temp\\AFS-Demo\\FOO.txt");

      if (stream == null)
        return NotFound();

      return File(stream, "application/octet-stream", "filetitle.txt"); // returns a FileStreamResult
    }
    //Swashbuckle.AspNetCore.Annotations.SwaggerOperationAttribute
   [HttpPost][SwaggerOperation(nameof(Test)+ 
      " -> Deis ist eine beschreibung die etwa 120 zeichen lang sein sollte und dann am ende mit drei punkten terminiert wird yes!",
      "//METHODENKONVENTION DOWNLOAD:     out fileName + out fileContentType + return Stream\r\n    // aktiviert wird regel druch druch return Stream\r\n    //    nicht vorhandensein des filename sorgt für <Snawflake44>.<endung aus contentType>\r\n    //    nicht vorhandensein des contentType sorgt für (application/octet-stream > endung: .dat)\r\n    // weitere out oder ref params verboten\r\n    //   jegliche in params folgen dem ujmw style\r\n    // + implizit wird ein zusätzlicher OVERLOAD, für GET -> alle in argumente werden dadruch zu query-params\r\n    //als protokoll wird das rohe senden von binärdaten (application/octet-stream) verwednet\r\n    // (in asp.net CORE mit dem IActionResult >> return File(stream, \"application/octet-stream\", \"filetitle.txt\");\r\n    //    ControllerBase.File(Stream fileStream, string contentType, string? fileDownloadName)\r\n"
      ), SwaggerResponse(200, "return-descriptio",null)]
    public Dto Test([FromBody][SwaggerRequestBody("BODY-DEX")][SwaggerParameter("param-descsdfdsfdfsdfsdfsfsf")] Dictionary<string,string> arghs) {
      return null;
    }


    //METHODENKONVENTION DOWNLOAD:     out fileName + out fileContentType + return Stream
    // aktiviert wird regel druch druch return Stream
    //    nicht vorhandensein des filename sorgt für <Snawflake44>.<endung aus contentType>
    //    nicht vorhandensein des contentType sorgt für (application/octet-stream > endung: .dat)
    // weitere out oder ref params verboten
    //   jegliche in params folgen dem ujmw style
    // + implizit wird ein zusätzlicher OVERLOAD, für GET -> alle in argumente werden dadruch zu query-params
    //als protokoll wird das rohe senden von binärdaten (application/octet-stream) verwednet
    // (in asp.net CORE mit dem IActionResult >> return File(stream, "application/octet-stream", "filetitle.txt");
    //    ControllerBase.File(Stream fileStream, string contentType, string? fileDownloadName)

    //METHODENKONVENTION UPLOAD:  file1 as Stream   in <file1>Name + in <file1>ContentType    void
    // aktiviert wird regel druch druch das vorhandensein mindestens eines in args vom typ stream
    //   jegliche in params sind automatisch in der query
    //   jegliche out oder ref params, solei ruturn folgen wieder dem UJMW style
    //als protokoll wird der multipart/form-data upload verwednet
    //   ( in asp.net core    IActionResult Upload(List<IFormFile> files)  )
    /*  
    HEADER:
        Content-Type: multipart/form-data; boundary="FILESTART"
    BODY:
        --FILESTART
        Content-Disposition: form-data; name="files"; filename="FOO.txt"
        Content-Type: text/plain

        Zeile1
        ä-ö-ü
        Zeile3
        --FILESTART--
     */

    // Links dazu:
    //   https://stackoverflow.com/questions/1131425/send-a-file-via-http-post-with-c-sharp
    //   https://stackoverflow.com/questions/3508338/what-is-the-boundary-in-multipart-form-data/20321259#20321259
    //   https://stackoverflow.com/questions/8659808/how-does-http-file-upload-work

    [HttpPost()]
    public  IActionResult Upload(List<IFormFile> files) {

      if (files == null) {
          return NotFound();
      }

      foreach (var formFile in files) {

        if (formFile.Length > 0) {
          var filePath = Path.GetTempFileName();

          using (var stream = System.IO.File.Create(filePath)) {
             formFile.CopyTo(stream);
          // '' formFile.OpenReadStream
          //    formFile.mi
          }
          return Ok(filePath);
        }

      }

      return Ok("EMPTY");
    }

  }
}
