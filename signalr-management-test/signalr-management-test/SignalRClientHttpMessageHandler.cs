using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Net.Http.Headers;

namespace signalr_management_test
{
    public class SignalRClientHttpMessageHandler : DelegatingHandler
    {
        private readonly HttpMessageHandler _externalHandler;
        private readonly CookieContainer _cookies = new CookieContainer();

        public SignalRClientHttpMessageHandler(HttpMessageHandler internalHandler, HttpMessageHandler externalHandler)
            : base(internalHandler)
        {
            _externalHandler = externalHandler;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var requestUri = request.RequestUri;
            request.Headers.Add(HeaderNames.Cookie, _cookies.GetCookieHeader(requestUri));

            HttpResponseMessage response;
            if (request.RequestUri.Host.StartsWith("localhost"))
            {
                response = await base.SendAsync(request, cancellationToken);
            }
            else
            {
                var initialSendAsyncMethodHandle = _externalHandler.GetType().GetMethod(
                    nameof(SendAsync),
                    BindingFlags.NonPublic | BindingFlags.Instance);
                response = await (Task<HttpResponseMessage>)initialSendAsyncMethodHandle.Invoke(
                    _externalHandler,
                    new object[] { request, cancellationToken });
            }

            if (response.Headers.TryGetValues(HeaderNames.SetCookie, out var setCookieHeaders))
            {
                foreach (var cookieHeader in SetCookieHeaderValue.ParseList(setCookieHeaders.ToList()))
                {
                    var cookie = new Cookie(cookieHeader.Name.Value, cookieHeader.Value.Value, cookieHeader.Path.Value);
                    if (cookieHeader.Expires.HasValue)
                    {
                        cookie.Expires = cookieHeader.Expires.Value.DateTime;
                    }
                    _cookies.Add(requestUri, cookie);
                }
            }

            return response;
        }
    }
}