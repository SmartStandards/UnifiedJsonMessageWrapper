using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeGeneration.Inspection {

  internal static class InspectionExtensions {

    public static string GetTypeNameSave(this Type extendee, out bool isNullable) {

      isNullable = (extendee.IsGenericType && extendee.GetGenericTypeDefinition() == typeof(Nullable<>));

      if (isNullable) {
        bool dummy;
        return extendee.GetGenericArguments()[0].GetTypeNameSave(out dummy);
      }
      else {
        return extendee.Name;
      }

    }

  }

}
