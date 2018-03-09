using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Configuration;
using System.Web.Script.Serialization;
using System.Web.UI;
using System.Web.UI.WebControls;

public partial class MyAPP_Test : System.Web.UI.Page
{
    //will need more bearers - this is only for testing - each client will have their own bearer

    private static String bearer = null;
    private static String name = null;
    private static String sensor = null;

    private static async Task<String> PostFormUrlEncoded<TResult>(string url, IEnumerable<KeyValuePair<string, string>> postData)
    {
        using (var httpClient = new HttpClient())
        {
            using (var content = new FormUrlEncodedContent(postData))
            {
                content.Headers.Clear();

                content.Headers.Add("Content-Type", "application/x-www-form-urlencoded");

                HttpResponseMessage response = await httpClient.PostAsync(url, content);

                return await response.Content.ReadAsStringAsync();
            }
        }
    }

    private static async Task<String> GetURL(string url, IEnumerable<KeyValuePair<string, string>> postData)
    {
        using (var httpClient = new HttpClient())
        {
            if (String.IsNullOrEmpty(bearer))
            {
                Login();
            }
            httpClient.DefaultRequestHeaders.Add("Authorization", bearer);
            //            httpClient.DefaultRequestHeaders."Content-Type"] =  "application/x-www-form-urlencoded";

            HttpResponseMessage response = await httpClient.GetAsync(url);

            return await response.Content.ReadAsStringAsync();
        }
    }

    private static void Login()
    {
        var args = new KeyValuePair<String, String>[] {
            new KeyValuePair<String, String>("grant_type",  WebConfigurationManager.AppSettings["grant_type"]),
            new KeyValuePair<String, String>("client_id", WebConfigurationManager.AppSettings["client_id"]),
            new KeyValuePair<String, String>("client_secret", WebConfigurationManager.AppSettings["client_secret"]),
        };
        String tokenResponse =
             (Task.Run(async ()
                => await PostFormUrlEncoded<String>("https://api.neur.io/v1/oauth2/token", args)))
             .Result;

        loginResponse resp = JsonConvert.DeserializeObject<loginResponse>(tokenResponse);
        bearer = String.Format("Bearer {0}", resp.access_token);
    }

    private static void getSensorID()
    {
        String tokenResponse =
             (Task.Run(async ()
                => await GetURL("https://api.neur.io/v1/users/current", null)))
             .Result;

        userResponse resp = JsonConvert.DeserializeObject<userResponse>(tokenResponse);
        sensor = resp.locations[0].sensors[0].SensorID;
        name = resp.name;
    }

    public class loginResponse
    {
        public string access_token { get; set; }
    }

    public class userResponse
    {
        public string name { get; set; }
        public string status { get; set; }
        public userResponse_Locations[] locations { get; set; }
    }

    public class userResponse_Locations
    {
        public string name { get; set; }
        public userResponse_Locations_Sensors[] sensors { get; set; }
    }

    public class userResponse_Locations_Sensors
    {
        public string SensorID { get; set; }
        public string sensorType { get; set; }
    }

    private List<SampleResponse> getSamples(DateTime StartDate)
    {
        var response = new List<SampleResponse>();
        var responseCount = 0;
        int i = 1;
        var retry = true;
        var startDate = StartDate.ToUniversalTime().ToString("o");
        var endDate = StartDate.AddMonths(1).ToUniversalTime().ToString("o");
        var url = "https://api.neur.io/v1/samples?sensorId={0}&start={1}&end={2}&granularity=hours&perPage=500&page={3}";
        while (retry)
        {
            String resp =
                 (Task.Run(async ()
                        => await GetURL(String.Format(url, sensor, startDate, endDate, i), null)))
                    .Result;

            response.AddRange(JsonConvert.DeserializeObject<SampleResponse[]>(resp));
            retry = responseCount != response.Count();
            responseCount = response.Count();
            i++;
        }
        return response;
    }

    protected void Page_LoadComplete(object sender, EventArgs e)
    {
        object now = Session["StartMonth"];

        getSensorID();

        if (now == null)
        {
            now = DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
                            rateStructure.timeZone), DateTimeKind.Local);
            now = DateTime.SpecifyKind(new DateTime(((DateTime)now).Year, ((DateTime)now).Month, 1), DateTimeKind.Local);
            Session["StartMonth"] = now;
        }

        var startMonth = (DateTime)now;

        var startOffset = 5;

        var start = startMonth.AddDays(startOffset);

        while (start > DateTime.Now)
            start = start.AddMonths(-1);


        Label1.Text = startMonth.ToLocalTime().ToString("MMMM");

        var samples = getSamples(start);

        Table tbl = new Table { CellPadding = 40, HorizontalAlign = HorizontalAlign.Center };

        foreach (var x in rateStructure.ratePeriods)
        {
            var tr = new TableRow();
            tr.Cells.Add(GetTable(false, x.periods, samples, x.title + " with Solar", ref x.totalWithSolar, x.generationRate, x.fuelSurcharge));
            tr.Cells.Add(GetTable(true, x.periods, samples, x.title + " without Solar", ref x.totalWithoutSolar, x.generationRate, x.fuelSurcharge));
            tbl.Rows.Add(tr);
        }
        PlaceHolder1.Controls.Add(tbl);
        Label2.Text = String.Format("Total savings = {0:$0.00}", rateStructure.ratePeriods.Last().totalWithoutSolar - rateStructure.ratePeriods.First().totalWithSolar);
    }
    //watt second to kwh
    private static int conversionFactor = (1000 * 60 * 60);


    private TableCell GetTable(bool IgnoreGeneration, RatePeriod[] ratePeriods, List<SampleResponse> samples, String title, ref Decimal totalCost, Decimal generationRate, Decimal fuelSurcharge)
    {
        SampleResponse prevSample = samples.First();

        Dictionary<Decimal, RatePeriodTotal> totalsByRatePeriod = ratePeriods.Select(x => x.rate + fuelSurcharge).Distinct().ToDictionary(x => x, x => new RatePeriodTotal());

        foreach (var sample in samples)
        {
            var ratePeriod = ratePeriods.FirstOrDefault(x => x.days != null && x.days.Contains(prevSample.intervalTS.DayOfWeek) && x.months.Contains(prevSample.intervalTS.Month) && x.startHour <= prevSample.intervalTS.Hour && x.endHour >= prevSample.intervalTS.Hour);

            if (ratePeriod == null) ratePeriod = ratePeriods.First();

            totalsByRatePeriod[ratePeriod.rate + fuelSurcharge].addValues(sample.consumption - prevSample.consumption, sample.generation - prevSample.generation);

            prevSample = sample;
        }

        TableRow tr;

        var table = new Table();
        tr = new TableHeaderRow { HorizontalAlign = HorizontalAlign.Center };
        tr.Cells.Add(new TableHeaderCell { Text = title });
        table.Rows.Add(tr);


        var tbl = new Table { GridLines = GridLines.Both, CellPadding = 4 };

        tr = new TableRow { HorizontalAlign = HorizontalAlign.Center, };
        tr.Cells.Add(new TableHeaderCell { Text = "Rate<br />(Cost/Net Usage)" });
        tr.Cells.Add(new TableHeaderCell { Text = "Consumption" });
        if (!IgnoreGeneration)
        {
            tr.Cells.Add(new TableHeaderCell { Text = "Generation" });
            tr.Cells.Add(new TableHeaderCell { Text = "Net Usage" });
            tr.Cells.Add(new TableHeaderCell { Text = "Effective Rate<br />(Cost/Consumption)" });
        }
        tr.Cells.Add(new TableHeaderCell { Text = "Cost" });

        tbl.Rows.Add(tr);

        decimal totalConsumption = 0;
        decimal totalGeneration = 0;
        totalCost = 0;

        foreach (var result in totalsByRatePeriod.Where(x => (x.Value.Consumption + x.Value.Generation) > 0))
        {
            var Generation = (IgnoreGeneration ? 0 : result.Value.Generation);
            totalConsumption += result.Value.Consumption;
            totalGeneration += Generation;

            var netConsumption = (result.Value.Consumption - Generation);
            var cost = netConsumption * (netConsumption < 0 ? generationRate : result.Key);

            totalCost += cost;

            if (ratePeriods.Count() > 1)
            {
                tr = new TableRow { HorizontalAlign = HorizontalAlign.Right };
                tr.Cells.Add(new TableCell { Text = result.Key.ToString("$.00") });
                tr.Cells.Add(new TableCell { Text = result.Value.Consumption.ToString("0.00") });
                if (!IgnoreGeneration)
                {
                    tr.Cells.Add(new TableCell { Text = Generation.ToString("0.00") });
                    tr.Cells.Add(new TableCell { Text = netConsumption.ToString("0.00") });
                    tr.Cells.Add(new TableCell { Text = (cost / (result.Value.Consumption)).ToString("$.000") });
                }
                tr.Cells.Add(new TableCell { Text = cost.ToString("$0.00") });
                tbl.Rows.Add(tr);
            }
        }

        var netUsage = totalConsumption - totalGeneration;
        tr = new TableFooterRow { HorizontalAlign = HorizontalAlign.Right, BackColor = Color.LightGray };
        tr.Cells.Add(new TableCell { Text = ((totalCost / netUsage) >= 0 ? (totalCost / netUsage).ToString("$.000") : " --- ") });
        tr.Cells.Add(new TableCell { Text = totalConsumption.ToString("0.00") });
        if (!IgnoreGeneration)
        {
            tr.Cells.Add(new TableCell { Text = totalGeneration.ToString("0.00") });
            tr.Cells.Add(new TableCell { Text = netUsage.ToString("0.00") });
            tr.Cells.Add(new TableCell { Text = (totalCost / totalConsumption).ToString("$.000") });
        }
        tr.Cells.Add(new TableCell { Text = (totalCost).ToString("$0.00") });
        tbl.Rows.Add(tr);

        tr = new TableRow();
        var tc = new TableCell();
        tc.Controls.Add(tbl);
        tr.Cells.Add(tc);
        table.Rows.Add(tr);

        tc = new TableCell();
        tc.Controls.Add(table);
        return tc;
    }
    public class SampleResponse
    {
        public DateTime intervalTS { get; private set; }
        public UInt64 consumption { get; private set; }
        public UInt64 generation { get; private set; }
        public UInt64 net { get; private set; }
        public string timestamp
        {
            set
            {
                intervalTS = DateTime.Parse(value);
            }
        }
        public string consumptionEnergy
        {
            set
            {
                consumption = UInt64.Parse(value);
            }
        }
        public string generationEnergy
        {
            set
            {
                generation = UInt64.Parse(value);
            }
        }
        public string netEnergy
        {
            set
            {
                net = UInt64.Parse(value);
            }
        }
    }

    private static DayOfWeek[] weekdays = new DayOfWeek[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };

    private RateStructure rateStructure = new RateStructure
    {
        ratePeriods = new RatePeriods[] {
        new RatePeriods{
            title = "Time of Day", 
            generationRate = 0.029M,
            fuelSurcharge =  .000535M,
            periods = new RatePeriod[] { 
                new RatePeriod{rate = 0.07400M},
                new RatePeriod{rate = 0.13660M, days = weekdays, startHour = 6, endHour = 22, months = new int[] {3,4,5,9,10,11}},
                new RatePeriod{rate = 0.13660M, days = weekdays, startHour = 6, endHour = 10, months = new int[] {6,7,8}},
                new RatePeriod{rate = 0.17900M, days = weekdays, startHour = 11, endHour = 18, months = new int[] {6,7,8}},
                new RatePeriod{rate = 0.13660M, days = weekdays, startHour = 19, endHour = 22, months = new int[] {6,7,8}},
                new RatePeriod{rate = 0.13660M, days = weekdays, startHour = 6, endHour = 16, months = new int[] {12, 1, 2}},
                new RatePeriod{rate = 0.17900M, days = weekdays, startHour = 17, endHour = 20, months = new int[] {12, 1, 2}},
                new RatePeriod{rate = 0.13660M, days = weekdays, startHour = 20, endHour = 22, months = new int[] {12, 1, 2}},
            } 
        },
        new RatePeriods{
            title = "Flat Rate", 
            generationRate = 0.029M,
            fuelSurcharge =  .000535M,
            periods = new RatePeriod[] { 
                new RatePeriod{rate = 0.11663M},
            } 
        }
    },
        timeZone = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time")
    };

    private class RateStructure
    {
        public RatePeriods[] ratePeriods { get; set; }
        public TimeZoneInfo timeZone { get; set; }
    }

    private class RatePeriods
    {
        public String title { get; set; }
        public Decimal totalWithSolar;
        public Decimal totalWithoutSolar;
        public Decimal generationRate { get; set; }
        public Decimal fuelSurcharge { get; set; }
        public RatePeriod[] periods { get; set; }
    }

    public class RatePeriod
    {
        public decimal rate { get; set; }
        public DayOfWeek[] days { get; set; }
        public int startHour { get; set; }
        public int endHour { get; set; }
        public int[] months { get; set; }
    }

    public class RatePeriodTotal
    {
        private UInt64 _consumption { get; set; }
        private UInt64 _generation { get; set; }

        public decimal Consumption
        {
            get
            {
                return Convert.ToDecimal(_consumption) / conversionFactor;
            }
        }

        public decimal Generation
        {
            get
            {
                return Convert.ToDecimal(_generation) / conversionFactor;
            }
        }

        public RatePeriodTotal()
        {
            _consumption = _generation = 0;
        }

        public void addValues(UInt64 consumption, UInt64 generation)
        {
            _consumption += consumption;
            _generation += generation;
        }
    }


    protected void Button1_Click(object sender, EventArgs e)
    {
        object obj = Session["StartMonth"];
        if (obj != null)
            Session["StartMonth"] = ((DateTime)obj).AddMonths(-1);

    }
    protected void Button2_Click(object sender, EventArgs e)
    {
        object obj = Session["StartMonth"];
        if (obj != null)
            Session["StartMonth"] = ((DateTime)obj).AddMonths(1);
    }
}