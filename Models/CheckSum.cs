using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Util01
{

    [Table("CheckSum")]
    public partial class CheckSum
    {
        public int Id { get; set; }

        [StringLength(200)]
        public string SHA { get; set; }

        [Required]
        [StringLength(200)]
        public string Folder { get; set; }

        [Required]
        [StringLength(100)]
        public string TheFileName { get; set; }

        [Required]
        [StringLength(10)]
        public string FileExt { get; set; }

        public int FileSize { get; set; }

        [Column(TypeName = "smalldatetime")]
        public DateTime FileCreateDt { get; set; }

        public int TimerMs { get; set; }

        [StringLength(200)]
        public string Notes { get; set; }

        public DateTime CreateDateTime { get; set; }

        [StringLength(20)]
        public string SCreateDateTime { get; set; }

    }
}
