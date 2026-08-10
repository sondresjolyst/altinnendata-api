namespace altinnendata_api.Infrastructure
{
    /// <summary>
    /// A vertical slice's HTTP surface. Implementations map their own route(s).
    /// </summary>
    public interface IEndpoint
    {
        void Map(IEndpointRouteBuilder app);
    }

    public static class EndpointExtensions
    {
        /// <summary>
        /// Discovers every <see cref="IEndpoint"/> in the app assembly and maps it.
        /// </summary>
        public static void MapEndpoints(this WebApplication app)
        {
            var endpointTypes = typeof(Program).Assembly.GetTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IEndpoint).IsAssignableFrom(t));

            foreach (var type in endpointTypes)
            {
                var endpoint = (IEndpoint)Activator.CreateInstance(type)!;
                endpoint.Map(app);
            }
        }
    }
}
