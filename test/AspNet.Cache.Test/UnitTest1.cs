using System.Threading.Tasks;
using AspNet.Cache.Test.WebServer;
using Xunit;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Diagnostics;

namespace AspNet.Cache.Test
{
    public class UnitTest1 : StartTestServer
    {
        [Fact]
        public async Task Test1()
        {
            var sw = new Stopwatch();
            sw.Start();
            var t = await client.GetAsync("/api/default");
            sw.Stop();

            var tempo = sw.ElapsedMilliseconds;

            var sw2 = new Stopwatch();
            sw2.Start();
            var t2 = await client.GetAsync("/api/default");
            sw2.Stop();

            var tempo2 = sw2.ElapsedMilliseconds;

            Assert.True(tempo > tempo2);

            var p = JsonConvert.DeserializeObject<IEnumerable<WeatherForecast>>(await t.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Get1()
        {
            var t = await client.GetAsync("/api/default/get1");
            var t2 = await client.GetAsync("/api/default/get1");

            var p = JsonConvert.DeserializeObject<IEnumerable<WeatherForecast>>(await t.Content.ReadAsStringAsync());

            Assert.True(t.IsSuccessStatusCode);
            Assert.True(t2.IsSuccessStatusCode);
        }
    }
}
