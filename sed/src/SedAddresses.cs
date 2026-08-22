namespace Icod.LineEditor.Sed;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Icod.CommandFramework.CommandLine;
using Icod.CommandFramework.Diagnostics;
using Icod.CommandFramework.IO;
using Icod.CommandFramework.Processes;

// Responsibility: address, range, and selection.
public static partial class Command {

	private readonly struct AddressContext {

		public long LineNumber {
			get;
		}

		public bool IsLastLine {
			get;
		}

		public string PatternSpace {
			get;
		}

		public CancellationToken CancellationToken {
			get;
		}

		public AddressContext(
			long lineNumber,
			bool isLastLine,
			string patternSpace,
			CancellationToken cancellationToken
		) {
			this.LineNumber = lineNumber;
			this.IsLastLine = isLastLine;
			this.PatternSpace = patternSpace;
			this.CancellationToken = cancellationToken;
		}

	}

	private abstract class Address {

		public virtual bool IsRegularExpression {
			get {
				return false;
			}
		}

		public abstract bool Matches(
			in AddressContext context
		);

		public virtual bool MatchesRangeEnd(
			in AddressContext context
		) {
			return this.Matches(
				context
			);
		}

	}

	private sealed class ZeroAddress : Address {

		public override bool Matches(
			in AddressContext context
		) {
			return false;
		}

	}

	private sealed class LineAddress : Address {

		public int LineNumber {
			get;
		}

		public LineAddress(
			int lineNumber
		) {
			if ( lineNumber <= 0 ) {
				throw new ArgumentOutOfRangeException(
					nameof( lineNumber )
				);
			}
			this.LineNumber = lineNumber;
		}

		public override bool Matches(
			in AddressContext context
		) {
			return context.LineNumber == this.LineNumber;
		}

		public override bool MatchesRangeEnd(
			in AddressContext context
		) {
			return context.LineNumber >= this.LineNumber;
		}

	}

	private sealed class StepAddress : Address {

		public int First {
			get;
		}

		public int Step {
			get;
		}

		public StepAddress(
			int first,
			int step
		) {
			if ( first < 0 ) {
				throw new ArgumentOutOfRangeException(
					nameof( first )
				);
			} else if ( step <= 0 ) {
				throw new ArgumentOutOfRangeException(
					nameof( step )
				);
			}

			this.First = first;
			this.Step = step;
		}

		public override bool Matches(
			in AddressContext context
		) {
			var first = 0 == this.First
				? this.Step
				: this.First
			;
			return (
				first <= context.LineNumber
				&& 0 == ( context.LineNumber - first ) % this.Step
			);
		}

	}

	private sealed class LastLineAddress : Address {

		public override bool Matches(
			in AddressContext context
		) {
			return context.IsLastLine;
		}

	}

	private sealed class RegexAddress : Address {

		private readonly SedCompiledRegularExpression myRegularExpression;

		public override bool IsRegularExpression {
			get {
				return true;
			}
		}

		public RegexAddress(
			SedCompiledRegularExpression regularExpression
		) {
			this.myRegularExpression = regularExpression
				?? throw new ArgumentNullException(
					nameof( regularExpression )
				)
			;
		}

		public override bool Matches(
			in AddressContext context
		) {
			return this.myRegularExpression.IsMatch(
				context.PatternSpace,
				context.CancellationToken
			);
		}

	}

	private abstract class RangeEnd {

		public abstract bool IsEnd(
			in AddressContext context,
			long rangeStartLine,
			bool isStartLine
		);

	}

	private sealed class AddressRangeEnd : RangeEnd {

		private readonly Address myAddress;

		public AddressRangeEnd(
			Address address
		) {
			this.myAddress = address ?? throw new ArgumentNullException(
				nameof( address )
			);
		}

		public override bool IsEnd(
			in AddressContext context,
			long rangeStartLine,
			bool isStartLine
		) {
			if (
				isStartLine
				&& this.myAddress.IsRegularExpression
			) {
				return false;
			}

			return this.myAddress.MatchesRangeEnd(
				context
			);
		}

	}

	private sealed class RelativeRangeEnd : RangeEnd {

		private readonly int myAdditionalLines;

		public RelativeRangeEnd(
			int additionalLines
		) {
			if ( additionalLines < 0 ) {
				throw new ArgumentOutOfRangeException(
					nameof( additionalLines )
				);
			}
			this.myAdditionalLines = additionalLines;
		}

		public override bool IsEnd(
			in AddressContext context,
			long rangeStartLine,
			bool isStartLine
		) {
			return context.LineNumber >= rangeStartLine + this.myAdditionalLines;
		}

	}

	private sealed class MultipleRangeEnd : RangeEnd {

		private readonly int myMultiple;

		public MultipleRangeEnd(
			int multiple
		) {
			if ( multiple <= 0 ) {
				throw new ArgumentOutOfRangeException(
					nameof( multiple )
				);
			}
			this.myMultiple = multiple;
		}

		public override bool IsEnd(
			in AddressContext context,
			long rangeStartLine,
			bool isStartLine
		) {
			return (
				!isStartLine
				&& 0 == context.LineNumber % this.myMultiple
			);
		}

	}

	private readonly struct Selection {

		public bool IsSelected {
			get;
		}

		public bool RangeEnded {
			get;
		}

		public bool RangeStarted {
			get;
		}

		public Selection(
			bool isSelected,
			bool rangeStarted,
			bool rangeEnded
		) {
			this.IsSelected = isSelected;
			this.RangeStarted = rangeStarted;
			this.RangeEnded = rangeEnded;
		}

	}

	private sealed class AddressSelector {

		private bool myRangeActive;
		private long myRangeStartLine;

		public Address? First {
			get;
		}

		public bool Negated {
			get;
		}

		public RangeEnd? Second {
			get;
		}

		public bool HasRange {
			get {
				return null != this.Second;
			}
		}

		public AddressSelector(
			Address? first,
			RangeEnd? second,
			bool negated
		) {
			if (
				null == first
				&& null != second
			) {
				throw new ArgumentException(
					"A range end requires a first address.",
					nameof( second )
				);
			}

			this.First = first;
			this.Second = second;
			this.Negated = negated;
			this.Reset();
		}

		public Selection Evaluate(
			in AddressContext context
		) {
			var rangeStarted = false;
			var rangeEnded = false;
			bool selected;

			if ( null == this.First ) {
				selected = true;
			} else if ( null == this.Second ) {
				selected = this.First.Matches(
					context
				);
			} else if ( this.myRangeActive ) {
				selected = true;
				if (
					this.Second.IsEnd(
						context,
						this.myRangeStartLine,
						isStartLine: false
					)
				) {
					this.myRangeActive = false;
					rangeEnded = true;
				}
			} else if (
				this.First is ZeroAddress
			) {
				selected = false;
			} else if (
				this.First.Matches(
					context
				)
			) {
				selected = true;
				rangeStarted = true;
				this.myRangeStartLine = context.LineNumber;
				this.myRangeActive = !this.Second.IsEnd(
					context,
					this.myRangeStartLine,
					isStartLine: true
				);
				rangeEnded = !this.myRangeActive;
			} else {
				selected = false;
			}

			return new Selection(
				this.Negated
					? !selected
					: selected,
				rangeStarted,
				rangeEnded
			);
		}

		public void Reset() {
			this.myRangeActive = this.First is ZeroAddress;
			this.myRangeStartLine = 0;
		}

	}


}
