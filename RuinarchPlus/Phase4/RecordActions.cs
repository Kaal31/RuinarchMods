using Ruinarch.ModContent;

namespace RuinarchPlus.Phase4
{
	/// <summary>
	/// The two things a villager does with a record (Phase4/Records.cs), as real actions:
	/// they walk up to a carrier (a Book Shelf, or a Book), spend an hour at it, and the
	/// finished action leaves a log on the carrier and the villager. Registered as new action
	/// types through the content framework (<see cref="ModContent.RegisterAction"/>).
	/// </summary>
	internal static class RecordActions
	{
		internal const string WriteId = "ruinarch.plus.write";
		internal const string ReadId = "ruinarch.plus.read";

		// One game hour.
		private const int Duration = 20;

		internal static INTERACTION_TYPE Write => ModContent.ActionTypeFor(WriteId);
		internal static INTERACTION_TYPE Read => ModContent.ActionTypeFor(ReadId);

		internal static void Register()
		{
			ModContent.RegisterAction(new ActionRegistration
			{
				Id = WriteId,
				Name = "WRITE_RECORD",
				Factory = () => new WriteRecord(),
				States = { new ActionState(WriteRecord.State, Duration, success: true, describe: n => Records.DescribeWrite(n.actor, n.poiTarget as TileObject)) }
			});
			ModContent.RegisterAction(new ActionRegistration
			{
				Id = ReadId,
				Name = "READ_RECORD",
				Factory = () => new ReadRecord(),
				States = { new ActionState(ReadRecord.State, Duration, success: true, describe: n => Records.DescribeRead(n.actor, n.poiTarget as TileObject)) }
			});
		}
	}

	/// <summary>Write what the villager remembers into the record the target carries.</summary>
	public class WriteRecord : GoapAction
	{
		internal const string State = "Write Success";

		public WriteRecord()
			: base(ModContent.ActionTypeFor(RecordActions.WriteId))
		{
			actionIconString = GoapActionStateDB.Read_Icon;
			actionLocationType = ACTION_LOCATION_TYPE.NEAR_TARGET;
			logTags = new[] { LOG_TAG.Work };
		}

		public override void Perform(ActualGoapNode goapNode)
		{
			base.Perform(goapNode);
			SetState(State, goapNode);
		}

		protected override int GetBaseCost(Character actor, IPointOfInterest target, JobQueueItem job, OtherData[] otherData)
		{
			return 10;
		}

		protected override bool AreRequirementsSatisfied(Character actor, IPointOfInterest poiTarget, OtherData[] otherData, JobQueueItem job)
		{
			return base.AreRequirementsSatisfied(actor, poiTarget, otherData, job) && Records.IsCarrier(poiTarget as TileObject);
		}

		// Called by name when the state finishes (GoapAction.CreateStates).
		public void AfterWriteSuccess(ActualGoapNode goapNode)
		{
			Records.Wrote(goapNode.actor, goapNode.poiTarget as TileObject);
		}
	}

	/// <summary>Read the record the target carries: remember every building it names.</summary>
	public class ReadRecord : GoapAction
	{
		internal const string State = "Read Success";

		public ReadRecord()
			: base(ModContent.ActionTypeFor(RecordActions.ReadId))
		{
			actionIconString = GoapActionStateDB.Read_Icon;
			actionLocationType = ACTION_LOCATION_TYPE.NEAR_TARGET;
			logTags = new[] { LOG_TAG.Work };
		}

		public override void Perform(ActualGoapNode goapNode)
		{
			base.Perform(goapNode);
			SetState(State, goapNode);
		}

		protected override int GetBaseCost(Character actor, IPointOfInterest target, JobQueueItem job, OtherData[] otherData)
		{
			return 10;
		}

		protected override bool AreRequirementsSatisfied(Character actor, IPointOfInterest poiTarget, OtherData[] otherData, JobQueueItem job)
		{
			return base.AreRequirementsSatisfied(actor, poiTarget, otherData, job) && Records.IsCarrier(poiTarget as TileObject);
		}

		public void AfterReadSuccess(ActualGoapNode goapNode)
		{
			Records.ReadAt(goapNode.actor, goapNode.poiTarget as TileObject);
		}
	}
}
