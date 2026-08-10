using MapsterMapper;
using altinnendata_api.Features.Users;
using altinnendata_api.Models;
using Xunit;

namespace altinnendata_api.Tests;

public class MappingProfileTests : TestBase
{
    private readonly IMapper _mapper = RealMapper;

    [Fact]
    public void InviteDto_To_User_UsesEmailAsUserName()
    {
        var dto = new InviteUserDto("sondre@example.com", "Sondre", "Sjølyst", "Admin");

        var user = _mapper.Map<User>(dto);

        Assert.Equal("sondre@example.com", user.UserName);
        Assert.Equal("sondre@example.com", user.Email);
        Assert.Equal("Sondre", user.FirstName);
        Assert.Equal("Sjølyst", user.LastName);
    }
}
