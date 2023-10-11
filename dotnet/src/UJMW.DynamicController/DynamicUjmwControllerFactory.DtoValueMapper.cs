using System.Diagnostics;
using System.Reflection;

namespace System.Web.UJMW {

  partial class DynamicUjmwControllerFactory {

    [DebuggerDisplay("DtoValueMapper {_ParameterName} ({_ParameterIndex})")]
    private class DtoValueMapper {

      private PropertyInfo _RequestDtoProp;
      private PropertyInfo _ResponseDtoProp;
      private string _ParameterName;
      private int _ParameterIndex;

      public DtoValueMapper(Type requestDtoType, Type responseDtoType, string parameterName, int parameterIndex) {
        _RequestDtoProp = requestDtoType.GetProperty(parameterName);
        _ResponseDtoProp = responseDtoType.GetProperty(parameterName);
        _ParameterName = parameterName;
        _ParameterIndex = parameterIndex;
      }

      public void MapRequestDtoToParam(object dto, object[] parameters) {
        if (_RequestDtoProp != null) {
          object value = _RequestDtoProp.GetValue(dto);
          parameters[_ParameterIndex] = value;
        }
      }

      public void MapParamToResponseDto(object[] parameters, object dto) {
        if (_ResponseDtoProp != null) {
          object value = parameters[_ParameterIndex];
          _ResponseDtoProp.SetValue(dto, value);
        }
      }

    }

  }

}
