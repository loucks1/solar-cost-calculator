using LiteDB;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization.Formatters.Binary;
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

    private static String dataLocation = WebConfigurationManager.AppSettings["data_location"];

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

    private List<SampleResponse> getSamples(DateTime startDate, DateTime endDate, LiteDatabase db)
    {
        var result = new List<SampleResponse>();
        while (startDate <= endDate && startDate <= DateTime.Now)
        {
            result.AddRange(getDailySample(startDate, db));
            startDate = startDate.AddDays(1);
        }
        return result;
    }

    private IEnumerable<SampleResponse> getDailySample(DateTime Date, LiteDatabase db)
    {
        var collectionName = Date.ToString("yyyyMMdd");

        if (db.CollectionExists(collectionName))
            return db.GetCollection<SampleResponse>(collectionName).FindAll();

        var response = new List<SampleResponse>();

        var url = "https://api.neur.io/v1/samples?sensorId={0}&start={1}&end={2}&granularity=hours&perPage=500";
        String resp =
             (Task.Run(async ()
                    => await GetURL(String.Format(url, sensor, Date.ToUniversalTime().ToString("o"), Date.AddDays(1).ToUniversalTime().ToString("o")), null)))
                .Result;

        response.AddRange(JsonConvert.DeserializeObject<SampleResponse[]>(resp));
        if (Date.AddDays(1) < DateTime.Now)
        {
            var collection = db.GetCollection<SampleResponse>(collectionName);
            collection.InsertBulk(response);
        }
        return response;
    }

    protected void Page_LoadComplete(object sender, EventArgs e)
    {
        getSensorID();

        IEnumerable<SampleResponse> samples;

        using (var db = new LiteDatabase(Path.Combine(dataLocation, sensor + ".db")))
        {
            var cycle = db.GetCollection<BillingCycle>("BillingCycles");

            DateTime startDate = DateTime.Now;
            DateTime endDate = DateTime.Now;

            var startOffset = 6;

            BillingCycle billingCycle = null;

            if (Session["Operation"] != null)
            {
                var cycles = cycle.FindAll().OrderBy(x => x.startDate).ToList();
                var index = cycles.IndexOf(cycles.First(x => x.Id == (int)Session["lastCycleID"]));
                switch (Session["Operation"].ToString())
                {
                    case "prev":
                        if (index > 0) index--;
                        break;
                    case "next":
                        if (index <= cycles.Count() - 2) index++;
                        break;
                }
                Session["Operation"] = null;
                billingCycle = cycles[index];
            }
            else
            {
                if (!DateTime.TryParse(TextBox1.Text, out startDate) || !DateTime.TryParse(TextBox2.Text, out endDate) || endDate <= startDate)
                {
                    var current = cycle.Find(x => x.endDate >= DateTime.Now && x.startDate <= DateTime.Now);
                    if (current.Count() > 0)
                    {
                        billingCycle = current.First();
                    }
                    else
                    {
                        startDate = DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
                                        rateStructure.timeZone), DateTimeKind.Local);
                        startDate = DateTime.SpecifyKind(new DateTime(((DateTime)startDate).Year, ((DateTime)startDate).Month, 1 + startOffset), DateTimeKind.Local);
                        endDate = startDate.AddMonths(1);
                    }
                }
                else
                {
                    startDate = DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(startDate, rateStructure.timeZone), DateTimeKind.Local);
                    endDate = DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(endDate, rateStructure.timeZone), DateTimeKind.Local);
                }
            }

            if (billingCycle != null)
            {
                startDate = billingCycle.startDate;
                endDate = billingCycle.endDate;
                Session["lastCycleID"] = billingCycle.Id;
            }
            TextBox1.Text = startDate.ToShortDateString();
            TextBox2.Text = endDate.ToShortDateString();


            Label1.Text = startDate.ToLocalTime().ToString("MMMM (MM/dd/yyyy - ") + endDate.ToLocalTime().ToString("MM/dd/yyyy)");

            samples = getSamples(startDate, endDate, db);

            if (cycle.Find(x => x.startDate == startDate && x.endDate == endDate).Count() == 0)
                cycle.Insert(new BillingCycle { startDate = startDate, endDate = endDate });
        }


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

    private TableCell GetTable(bool IgnoreGeneration, RatePeriod[] ratePeriods, IEnumerable<SampleResponse> samples, String title, ref Decimal totalCost, Decimal generationRate, Decimal fuelSurcharge)
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

    public class BillingCycle
    {
        public int Id { get; set; }
        public DateTime startDate { get; set; }
        public DateTime endDate { get; set; }
    }

    public class SampleResponse
    {
        public int Id { get; set; }
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
            generationRate = 0.0334M,
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
            generationRate = 0.0334M,
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
        Session["Operation"] = "prev";
    }
    protected void Button2_Click(object sender, EventArgs e)
    {
        Session["Operation"] = "next";
    }

    protected void Button3_Click(object sender, EventArgs e)
    {
    }

}