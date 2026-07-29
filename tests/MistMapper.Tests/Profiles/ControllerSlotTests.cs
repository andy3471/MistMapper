using MistMapper.Shared;

namespace MistMapper.Tests.Profiles;

public sealed class ControllerSlotTests
{
    [Fact]
    public void Slots_default_to_enabled()
    {
        var slot = new ControllerSlot { DeviceKey = "dev-1", DriverId = "test-driver", Order = 0 };

        slot.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Slots_can_be_ordered()
    {
        var slots = new List<ControllerSlot>
        {
            new() { DeviceKey = "c", Order = 2 },
            new() { DeviceKey = "a", Order = 0 },
            new() { DeviceKey = "b", Order = 1 },
        };

        var ordered = slots.OrderBy(s => s.Order).Select(s => s.DeviceKey).ToList();

        ordered.Should().ContainInOrder("a", "b", "c");
    }

    [Fact]
    public void Reorder_slots_reassigns_order_values()
    {
        var slots = new List<ControllerSlot>
        {
            new() { DeviceKey = "first", Order = 5 },
            new() { DeviceKey = "second", Order = 10 },
            new() { DeviceKey = "third", Order = 15 },
        };

        var reordered = slots.Select((s, i) => new ControllerSlot
        {
            DeviceKey = s.DeviceKey,
            DriverId = s.DriverId,
            Order = i,
            ProfileId = s.ProfileId,
            DisplayName = s.DisplayName,
            Enabled = s.Enabled
        }).ToList();

        reordered[0].Order.Should().Be(0);
        reordered[1].Order.Should().Be(1);
        reordered[2].Order.Should().Be(2);
    }

    [Fact]
    public void ProfileStoreDocument_includes_controller_slots()
    {
        var doc = new ProfileStoreDocument();
        doc.ControllerSlots.Add(new ControllerSlot
        {
            Order = 0,
            DeviceKey = "hid-path-1",
            DriverId = "steam-controller",
            ProfileId = "profile-1",
            DisplayName = "My Controller",
            LastModel = "sc2"
        });

        doc.ControllerSlots.Should().HaveCount(1);
        doc.ControllerSlots[0].DeviceKey.Should().Be("hid-path-1");
        doc.ControllerSlots[0].DriverId.Should().Be("steam-controller");
        doc.ControllerSlots[0].ProfileId.Should().Be("profile-1");
    }

    [Fact]
    public void Slot_can_have_null_profile_for_unassigned()
    {
        var slot = new ControllerSlot { DeviceKey = "test", DriverId = "test", Order = 0 };

        slot.ProfileId.Should().BeNull();
    }
}
