using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace MyWealthV2.Application.FunctionalTests.Bff;

/// <summary>
/// TestServer accepts body writes on any status. Kestrel throws when anything, even an empty
/// buffer, is written to a 204, 205 or 304 response. This filter applies the Kestrel rule so
/// BFF tests see the same failure as the real host.
/// </summary>
internal sealed class KestrelBodyRules : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, inner) =>
        {
            var original = context.Response.Body;
            context.Response.Body = new NoBodyGuardStream(original, context.Response);
            try
            {
                await inner(context);
            }
            finally
            {
                context.Response.Body = original;
            }
        });
        next(app);
    };

    private sealed class NoBodyGuardStream(Stream inner, HttpResponse response) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            Guard();
            inner.Write(buffer, offset, count);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Guard();
            return inner.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Guard();
            return inner.WriteAsync(buffer, cancellationToken);
        }

        private void Guard()
        {
            if (response.StatusCode is StatusCodes.Status204NoContent
                or StatusCodes.Status205ResetContent
                or StatusCodes.Status304NotModified)
            {
                throw new InvalidOperationException(
                    $"Writing to the response body is invalid for responses with status code {response.StatusCode}.");
            }
        }
    }
}
