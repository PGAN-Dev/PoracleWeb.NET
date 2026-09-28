using System.Globalization;
using System.Text.Json;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class PhotonGeocodingTests
{
    // Captured from a live Photon instance (komoot/photon, US extract): /reverse?lat=41.652813&lon=-83.53721&limit=1.
    // Note "locality" rather than "district", and no "housenumber" on a named building.
    private const string LiveReverse = /*lang=json,strict*/ """
        {"type":"FeatureCollection","features":[{"type":"Feature","properties":{"osm_type":"W","osm_id":189371898,
          "osm_key":"building","osm_value":"commercial","type":"house","name":"Nicholas Building",
          "street":"North Huron Street","locality":"Downtown","city":"Toledo","county":"Lucas","state":"Ohio",
          "country":"United States","postcode":"43604","countrycode":"US",
          "extent":[-83.5372817,41.6529317,-83.5366198,41.6524296]},
          "geometry":{"type":"Point","coordinates":[-83.5369508,41.6526807]}}]}
        """;

    // Same instance: /api?q=Imagination%20Station%20Toledo&limit=2.
    private const string LiveSearch = /*lang=json,strict*/ """
        {"type":"FeatureCollection","features":[{"type":"Feature","properties":{"osm_type":"W","osm_id":189303388,
          "osm_key":"tourism","osm_value":"museum","type":"house","housenumber":"1","name":"Imagination Station",
          "street":"Discovery Way","locality":"Downtown","city":"Toledo","county":"Lucas","state":"OH",
          "country":"United States","postcode":"43659","countrycode":"US",
          "extent":[-83.5323282,41.652316,-83.5307976,41.6515873]},
          "geometry":{"type":"Point","coordinates":[-83.531463,41.6519471]}}]}
        """;

    [Fact]
    public void BuildsReverseUrlWithInvariantCoordinates()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            // A comma decimal separator must not leak into the query string.
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal(
                "http://photon:2322/reverse?lat=41.652813&lon=-83.53721&limit=1",
                PhotonGeocoding.BuildReverseUrl("http://photon:2322/", 41.652813, -83.53721));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void BuildsSearchUrlWithEscapedQuery() =>
        Assert.Equal(
            "http://photon:2322/api?q=420%20Madison%20Ave%2C%20Toledo&limit=5",
            PhotonGeocoding.BuildSearchUrl("http://photon:2322", "420 Madison Ave, Toledo", 5));

    [Fact]
    public void MapsLiveReverseResultToNominatimShape()
    {
        using var doc = JsonDocument.Parse(PhotonGeocoding.ToNominatimReverse(LiveReverse));
        var root = doc.RootElement;

        Assert.Equal(
            "Nicholas Building, North Huron Street, Downtown, Toledo, Lucas, Ohio, 43604, United States",
            root.GetProperty("display_name").GetString());
        Assert.Equal("Nicholas Building", root.GetProperty("name").GetString());
        var address = root.GetProperty("address");
        Assert.Equal("North Huron Street", address.GetProperty("road").GetString());
        Assert.Equal("Downtown", address.GetProperty("suburb").GetString());
        Assert.Equal("Toledo", address.GetProperty("city").GetString());
        Assert.Equal("Ohio", address.GetProperty("state").GetString());
        Assert.Equal("43604", address.GetProperty("postcode").GetString());
        Assert.Equal("us", address.GetProperty("country_code").GetString());
        Assert.False(address.TryGetProperty("house_number", out _));
        // A reverse answer carries no coordinates, as Nominatim's reverse shape is read by the SPA.
        Assert.False(root.TryGetProperty("lat", out _));
    }

    [Fact]
    public void MapsLiveSearchResultWithStringCoordinates()
    {
        using var doc = JsonDocument.Parse(PhotonGeocoding.ToNominatimSearch(LiveSearch));
        var result = Assert.Single(doc.RootElement.EnumerateArray());

        // GeoJSON is [lon, lat]; Nominatim sends lat and lon as strings.
        Assert.Equal("41.6519471", result.GetProperty("lat").GetString());
        Assert.Equal("-83.531463", result.GetProperty("lon").GetString());
        Assert.Equal("1", result.GetProperty("address").GetProperty("house_number").GetString());
        Assert.Equal("Discovery Way", result.GetProperty("address").GetProperty("road").GetString());
        Assert.Equal(
            "Imagination Station, 1, Discovery Way, Downtown, Toledo, Lucas, OH, 43659, United States",
            result.GetProperty("display_name").GetString());
    }

    // A street feature names itself in "name" and has no "street" property.
    [Fact]
    public void UsesNameAsRoadForStreetFeatures()
    {
        const string json = /*lang=json,strict*/ """
            {"features":[{"geometry":{"coordinates":[-83.5,41.6]},
              "properties":{"type":"street","name":"Summit Street","city":"Toledo","country":"United States"}}]}
            """;

        using var doc = JsonDocument.Parse(PhotonGeocoding.ToNominatimReverse(json));

        Assert.Equal("Summit Street", doc.RootElement.GetProperty("address").GetProperty("road").GetString());
        // Listed once even though it is both the name and the road.
        Assert.Equal("Summit Street, Toledo, United States", doc.RootElement.GetProperty("display_name").GetString());
    }

    [Fact]
    public void UsesNameAsCityForCityFeatures()
    {
        const string json = /*lang=json,strict*/ """
            {"features":[{"geometry":{"coordinates":[-84.55,42.73]},
              "properties":{"type":"city","name":"Lansing","state":"Michigan","country":"United States"}}]}
            """;

        using var doc = JsonDocument.Parse(PhotonGeocoding.ToNominatimSearch(json));
        var result = Assert.Single(doc.RootElement.EnumerateArray());

        Assert.Equal("Lansing", result.GetProperty("address").GetProperty("city").GetString());
    }

    [Theory]
    [InlineData(/*lang=json,strict*/ """{"type":"FeatureCollection","features":[]}""")]
    [InlineData(/*lang=json,strict*/ """{"type":"FeatureCollection"}""")]
    [InlineData(/*lang=json,strict*/ """{"features":[{"properties":{}}]}""")]
    [InlineData(/*lang=json,strict*/ """{"features":[null,"x"]}""")]
    [InlineData(/*lang=json,strict*/ "[]")]
    public void ReverseWithNothingFoundAnswersLikeNominatim(string json)
    {
        using var doc = JsonDocument.Parse(PhotonGeocoding.ToNominatimReverse(json));

        Assert.Equal("Unable to geocode", doc.RootElement.GetProperty("error").GetString());
        Assert.False(doc.RootElement.TryGetProperty("display_name", out _));
    }

    [Theory]
    [InlineData(/*lang=json,strict*/ """{"type":"FeatureCollection","features":[]}""")]
    [InlineData(/*lang=json,strict*/ "[]")]
    public void SearchWithNothingFoundIsAnEmptyArray(string json) =>
        Assert.Equal("[]", PhotonGeocoding.ToNominatimSearch(json));

    [Fact]
    public void SkipsSearchResultsWithoutUsableCoordinates()
    {
        const string json = /*lang=json,strict*/ """
            {"features":[
              {"properties":{"name":"Nowhere"}},
              {"geometry":{"coordinates":["x","y"]},"properties":{"name":"Bad coords"}},
              {"geometry":null,"properties":{"name":"Null geometry"}},
              {"geometry":[-83.5,41.6],"properties":{"name":"Array geometry"}},
              null,
              7,
              {"geometry":{"coordinates":[-83.5,41.6]},"properties":{"name":"Toledo","type":"city"}}]}
            """;

        using var doc = JsonDocument.Parse(PhotonGeocoding.ToNominatimSearch(json));

        Assert.Equal("Toledo", Assert.Single(doc.RootElement.EnumerateArray()).GetProperty("name").GetString());
    }
}
