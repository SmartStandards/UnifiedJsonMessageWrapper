using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Security.Controllers {

  [Route("afs")]
  public class DownloadUploadDemo : Controller {

    //konvention  return stream out string fileName / out string mimeType

    [HttpGet()]
    public IActionResult Download() {
      Stream stream = System.IO.File.OpenRead("C:\\Temp\\AFS-Demo\\FOO.txt");

      if (stream == null)
        return NotFound();

      return File(stream, "application/octet-stream", "filetitle.txt"); // returns a FileStreamResult
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
