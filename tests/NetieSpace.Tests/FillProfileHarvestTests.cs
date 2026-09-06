using System.Text.Json;
using NetieSpace.Services;

namespace NetieSpace.Tests;

/// <summary>
/// The viewer harvests PII on every PDF flush. These pin that the harvest is a
/// *proposal* the user has to confirm, and that it cannot widen the store.
/// </summary>
public class FillProfileHarvestTests
{
    private static FillProfileStore.Profile Current(
        string name = "Ada Lovelace",
        string email = "ada@example.com",
        string phone = "",
        string address = "",
        string dob = "",
        string nationality = "",
        string dateFormat = "dd-MMM-yyyy") =>
        new(name, email, phone, address, dob, nationality, dateFormat, "05/09/2026");

    private static JsonElement Harvest(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();

    // The defect: FillProfileStore.MergeHarvest was called on every save, so the store
    // wrote itself with no confirm. Propose replaces it and must never reach disk - if
    // an unconfirmed write API comes back, this fails.
    [Fact]
    public void Store_exposes_no_unconfirmed_merge()
    {
        Assert.Null(typeof(FillProfileStore).GetMethod("MergeHarvest"));
        Assert.DoesNotContain(
            typeof(FillProfileHarvest).GetMethods(),
            m => m.Name is "Save" or "Commit" or "MergeHarvest" or "Write");
    }

    [Fact]
    public void Propose_returns_the_changed_fields_without_saving()
    {
        var proposal = FillProfileHarvest.Propose(
            Harvest("""{"phone":"+60 12-345 6789","nationality":"Malaysian"}"""),
            Current());

        Assert.NotNull(proposal);
        Assert.Equal(2, proposal!.Changes.Count);
        Assert.Equal("+60 12-345 6789", proposal.Merged.Phone);
        Assert.Equal("Malaysian", proposal.Merged.Nationality);
        // Untouched fields are carried, not cleared.
        Assert.Equal("Ada Lovelace", proposal.Merged.FullName);
        Assert.Equal("ada@example.com", proposal.Merged.Email);
    }

    [Fact]
    public void Propose_returns_null_when_nothing_is_new()
    {
        // Same values back from the viewer must not raise a prompt.
        var proposal = FillProfileHarvest.Propose(
            Harvest("""{"name":"Ada Lovelace","email":"ada@example.com"}"""),
            Current());

        Assert.Null(proposal);
    }

    [Fact]
    public void Propose_ignores_keys_outside_the_bounded_set()
    {
        // fill-profile.bin does not grow because the viewer invented a key.
        var proposal = FillProfileHarvest.Propose(
            Harvest("""{"ssn":"012345-06-7890","passport":"A1234567","salary":"9000"}"""),
            Current());

        Assert.Null(proposal);
    }

    [Fact]
    public void Propose_keeps_existing_values_for_blank_and_non_string_fields()
    {
        var proposal = FillProfileHarvest.Propose(
            Harvest("""{"name":"   ","email":12345,"phone":null,"address":"12 Jalan Ampang"}"""),
            Current());

        Assert.NotNull(proposal);
        Assert.Single(proposal!.Changes);
        Assert.Equal("Address", proposal.Changes[0].Label);
        Assert.Equal("Ada Lovelace", proposal.Merged.FullName);
        Assert.Equal("ada@example.com", proposal.Merged.Email);
        Assert.Equal("", proposal.Merged.Phone);
    }

    [Fact]
    public void Propose_returns_null_for_a_non_object_harvest()
    {
        Assert.Null(FillProfileHarvest.Propose(Harvest("\"Ada\""), Current()));
        Assert.Null(FillProfileHarvest.Propose(Harvest("[]"), Current()));
        Assert.Null(FillProfileHarvest.Propose(Harvest("null"), Current()));
    }

    [Fact]
    public void Describe_names_every_field_it_would_keep()
    {
        var proposal = FillProfileHarvest.Propose(
            Harvest("""{"email":"ada@netie.ai","dob":"10/12/1815"}"""),
            Current());

        var body = proposal!.Describe();
        Assert.Contains("Email: ada@netie.ai", body);
        Assert.Contains("Date of birth: 10/12/1815", body);
        // Disclosure is exactly the changed fields - no unlisted extras.
        Assert.Equal(proposal.Changes.Count, body.Split('\n').Length);
    }

    [Fact]
    public void Changes_carry_the_value_being_replaced()
    {
        var proposal = FillProfileHarvest.Propose(
            Harvest("""{"name":"Grace Hopper"}"""),
            Current());

        var change = Assert.Single(proposal!.Changes);
        Assert.Equal("Name", change.Label);
        Assert.Equal("Ada Lovelace", change.OldValue);
        Assert.Equal("Grace Hopper", change.NewValue);
    }
}
