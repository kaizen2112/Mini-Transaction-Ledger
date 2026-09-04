var builder = WebApplication.CreateBuilder(args);

// Controllers, not minimal APIs: the HTTP surface stays declarative and
// attribute-routed, and every endpoint has one obvious home on disk.
builder.Services.AddControllers();

var app = builder.Build();

// Pipeline order, as blocks land:
//   1. global exception handler -> problem+json   (B1 part 2)
//   2. swagger, Development only                  (B1 part 2)
//   3. UseRouting                                 (added implicitly, here)
//   4. UseCors                                    (B13, if needed)
//   5. UseAuthentication                          (B4)
//   6. UseAuthorization                           (B4)
//   7. MapControllers                             (terminal)
//
// No UseHttpsRedirection: in Compose the API listens on plain :8080 with no
// certificate and the container healthcheck curls http://localhost:8080/health
// (docs/08-docker.md §5). TLS terminates at the proxy in a real deployment.
app.MapControllers();

app.Run();
